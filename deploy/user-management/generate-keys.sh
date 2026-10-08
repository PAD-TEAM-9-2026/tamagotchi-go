#!/bin/sh
# Creates the key User Management signs its access tokens with (RS256, 2048 bits). Run once, from anywhere, before
# `docker compose up`. The file stays local: deploy/secrets/ is git-ignored, and the private key must never be
# committed or shared. It is never overwritten, so a key in use is not replaced by mistake.
set -eu

cd "$(dirname "$0")/.."

mkdir -p secrets
if [ -f secrets/access-token.pem ]; then
  echo "secrets/access-token.pem already exists, leaving it as it is"
else
  openssl genrsa -out secrets/access-token.pem 2048
  # The service runs as a non-root user inside its container and must be able to read the key.
  chmod 644 secrets/access-token.pem
  echo "created secrets/access-token.pem"
fi

KEY_ID="user-management-access-1"
if [ -f .env ]; then
  KEY_ID=$(grep '^USER_MANAGEMENT_ACCESS_TOKEN_KEY_ID=' .env | head -1 | cut -d= -f2-)
  KEY_ID="${KEY_ID:-user-management-access-1}"
fi
echo "key id: $KEY_ID (USER_MANAGEMENT_ACCESS_TOKEN_KEY_ID in .env)"
