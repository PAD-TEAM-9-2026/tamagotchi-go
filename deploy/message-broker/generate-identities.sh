#!/bin/sh
# Creates the stack's mTLS workload identities and the broker's context signing key
# (contracts/identity.md sections 1 and 6) for the message-broker candidate. Run once,
# from anywhere, before `docker compose -f compose.yaml -f compose.broker.yaml up`.
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
const { readFileSync, writeFileSync } = require("node:fs");
const { createPublicKey } = require("node:crypto");
const jwk = createPublicKey(readFileSync("secrets/broker-context.pem")).export({ format: "jwk" });
const kid = process.argv[1];
writeFileSync("secrets/broker-context-jwks.json", JSON.stringify({ keys: [{ ...jwk, kid, alg: "RS256", use: "sig" }] }, null, 2) + "\n");
' "$KEY_ID"
echo "wrote secrets/broker-context-jwks.json (kid: $KEY_ID)"
