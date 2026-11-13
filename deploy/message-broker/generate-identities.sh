#!/bin/sh
# Creates the stack's mTLS workload identities and the broker's context signing key
# (contracts/identity.md sections 1 and 6). Run once, from anywhere, before the first
# `docker compose up`.
# Everything stays in deploy/secrets/, which is git-ignored. Existing files are kept,
# so running it again never replaces a working certificate or key.
set -eu

cd "$(dirname "$0")/.."
if [ -f .env ]; then
  KEY_ID=$(grep '^BROKER_CONTEXT_KEY_ID=' .env | head -1 | cut -d= -f2-)
fi
KEY_ID="${KEY_ID:-broker-context-1}"
DAYS=365
WORKLOADS="message-broker gateway user-management tamagotchi battle guild package-registry map monster-raid notification"

# Validate all existing material before creating anything. Never repair by rotation.
check_pair() {
  key=$1; cert=$2
  if [ -e "$key" ] || [ -e "$cert" ]; then
    [ -f "$key" ] && [ ! -L "$key" ] && [ -f "$cert" ] && [ ! -L "$cert" ] || {
      echo "incomplete or unsafe identity pair: $key" >&2; exit 2;
    }
    key_public=$(openssl pkey -in "$key" -pubout 2>/dev/null)
    cert_public=$(openssl x509 -in "$cert" -pubkey -noout 2>/dev/null)
    [ "$key_public" = "$cert_public" ] || { echo "identity key mismatch: $cert" >&2; exit 2; }
    openssl x509 -in "$cert" -checkend 0 -noout >/dev/null || exit 2
  fi
}
check_pair secrets/ca/ca.key secrets/tls/ca.crt
if [ -f secrets/tls/ca.crt ]; then
  openssl verify -CAfile secrets/tls/ca.crt secrets/tls/ca.crt >/dev/null
fi
for name in $WORKLOADS; do
  check_pair "secrets/tls/$name.key" "secrets/tls/$name.crt"
  if [ -f "secrets/tls/$name.crt" ]; then
    [ -f secrets/tls/ca.crt ] || { echo "missing CA for existing identity" >&2; exit 2; }
    openssl verify -CAfile secrets/tls/ca.crt -verify_hostname "$name" "secrets/tls/$name.crt" >/dev/null
    sans=$(openssl x509 -in "secrets/tls/$name.crt" -noout -ext subjectAltName)
    printf '%s' "$sans" | grep -Eq "URI:spiffe://tamagotchi-go/workload/$name(,|$)" || {
      echo "wrong workload identity: $name" >&2; exit 2;
    }
  fi
done
# Verify the signer and its exact JWKS before generating any missing workloads.
node -e '
const { existsSync, readFileSync, lstatSync } = require("node:fs");
const { createPublicKey } = require("node:crypto");
const pem = "secrets/broker-context.pem", json = "secrets/broker-context-jwks.json";
if (existsSync(pem) !== existsSync(json)) throw Error("incomplete Broker signer pair");
if (existsSync(pem)) {
  if (lstatSync(pem).isSymbolicLink() || lstatSync(json).isSymbolicLink()) throw Error("unsafe signer path");
  const pub = createPublicKey(readFileSync(pem)).export({format:"jwk"});
  const keys = JSON.parse(readFileSync(json)).keys;
  if (!Array.isArray(keys) || keys.length !== 1) throw Error("unexpected signer JWKS");
  const key = keys[0];
  if (key.kid !== process.argv[1] || key.kty !== "RSA" || key.alg !== "RS256" || key.use !== "sig" || key.n !== pub.n || key.e !== pub.e || "d" in key) throw Error("Broker JWKS mismatch; refusing replacement");
}
' "$KEY_ID"

mkdir -p secrets/ca secrets/tls
if [ ! -f secrets/ca/ca.key ]; then
  openssl req -x509 -newkey rsa:2048 -nodes -keyout secrets/ca/ca.key -out secrets/tls/ca.crt \
    -subj "/CN=tamagotchi-go stack CA" -days "$DAYS"
  echo "created the stack CA; its key stays in secrets/ca/ and is never mounted"
fi

for name in $WORKLOADS; do
  [ -f "secrets/tls/$name.crt" ] && continue
  openssl req -newkey rsa:2048 -nodes -keyout "secrets/tls/$name.key" -out "secrets/tls/$name.csr" -subj "/CN=$name"
  # One URI SAN with the workload identity, and the canonical name as the server name.
  printf 'subjectAltName=DNS:%s,URI:spiffe://tamagotchi-go/workload/%s\n' "$name" "$name" >"secrets/tls/$name.ext"
  openssl x509 -req -in "secrets/tls/$name.csr" -CA secrets/tls/ca.crt -CAkey secrets/ca/ca.key \
    -CAserial secrets/ca/ca.srl -CAcreateserial -out "secrets/tls/$name.crt" -days "$DAYS" \
    -extfile "secrets/tls/$name.ext"
  rm "secrets/tls/$name.csr" "secrets/tls/$name.ext"
  # Workloads run as non-root users inside their containers and must be able to read their key.
  chmod 644 "secrets/tls/$name.key"
  echo "created secrets/tls/$name.crt"
done

if [ ! -f secrets/broker-context.pem ]; then
  openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -out secrets/broker-context.pem
  chmod 644 secrets/broker-context.pem
  echo "created secrets/broker-context.pem"
fi

node -e '
const { readFileSync, writeFileSync, existsSync } = require("node:fs");
const { createPublicKey } = require("node:crypto");
const jwk = createPublicKey(readFileSync("secrets/broker-context.pem")).export({ format: "jwk" });
const kid = process.argv[1];
if (!existsSync("secrets/broker-context-jwks.json")) writeFileSync("secrets/broker-context-jwks.json", JSON.stringify({ keys: [{ ...jwk, kid, alg: "RS256", use: "sig" }] }, null, 2) + "\n");
' "$KEY_ID"
echo "validated secrets/broker-context-jwks.json (kid: $KEY_ID)"
