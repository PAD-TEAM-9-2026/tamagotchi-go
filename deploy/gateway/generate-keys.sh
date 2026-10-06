#!/bin/sh
# Creates the Gateway's assertion signing key and the public key set that services verify with.
# Run once, from anywhere, before `docker compose up`. Both files stay local: deploy/secrets/
# is git-ignored, and the private key must never be committed or shared.
set -eu

cd "$(dirname "$0")/.."
if [ -f .env ]; then
  KEY_ID=$(grep '^GATEWAY_ASSERTION_KEY_ID=' .env | head -1 | cut -d= -f2-)
fi
KEY_ID="${KEY_ID:-gateway-assertion-1}"

mkdir -p secrets
if [ ! -f secrets/gateway-assertion.pem ]; then
  openssl genrsa -out secrets/gateway-assertion.pem 2048
  # The Gateway runs as a non-root user inside its container and must be able to read the key.
  chmod 644 secrets/gateway-assertion.pem
  echo "created secrets/gateway-assertion.pem"
fi

node -e '
const { readFileSync, writeFileSync } = require("node:fs");
const { createPublicKey } = require("node:crypto");
const jwk = createPublicKey(readFileSync("secrets/gateway-assertion.pem")).export({ format: "jwk" });
const kid = process.argv[1];
writeFileSync("secrets/gateway-assertion-jwks.json", JSON.stringify({ keys: [{ ...jwk, kid, alg: "RS256", use: "sig" }] }, null, 2) + "\n");
' "$KEY_ID"
echo "wrote secrets/gateway-assertion-jwks.json (kid: $KEY_ID)"
