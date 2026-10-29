#!/bin/sh
# Creates a local development CA and a Redis server certificate signed by it, so the local stack
# talks to Redis over TLS like every other environment (SECURITY.md: app <-> Redis uses ssl=true).
# Output goes to docker/redis/tls/, which is git-ignored: keys and certificates never go in Git.
# Run once per machine, before `docker compose up`:
#   ./scripts/generate-dev-certs.sh
# Pass --force to replace existing files (e.g. after the certificate expires).
set -e

DIR="$(cd "$(dirname "$0")/.." && pwd)/docker/redis/tls"
DAYS=825

if [ -f "$DIR/redis.crt" ] && [ "$1" != "--force" ]; then
  echo "Certificates already exist in $DIR (use --force to regenerate)."
  exit 0
fi

mkdir -p "$DIR"
umask 077
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

# Local CA (EC P-256). Only this machine trusts it, and only through Redis:TlsCaCertificatePath.
openssl ecparam -name prime256v1 -genkey -noout -out "$DIR/ca.key"
openssl req -x509 -new -key "$DIR/ca.key" -sha256 -days "$DAYS" \
  -subj "/CN=Malabak Local Dev CA" \
  -addext "basicConstraints=critical,CA:TRUE,pathlen:0" \
  -addext "keyUsage=critical,keyCertSign,cRLSign" \
  -out "$DIR/ca.crt"

# Server certificate for Redis: reachable as localhost from the host and as "redis" inside compose.
openssl ecparam -name prime256v1 -genkey -noout -out "$TMP/redis.key"
openssl pkcs8 -topk8 -nocrypt -in "$TMP/redis.key" -out "$DIR/redis.key"
openssl req -new -key "$DIR/redis.key" -subj "/CN=localhost" -out "$TMP/redis.csr"
cat > "$TMP/redis.ext" <<EOF
basicConstraints=critical,CA:FALSE
keyUsage=critical,digitalSignature
extendedKeyUsage=serverAuth
subjectAltName=DNS:localhost,DNS:redis,IP:127.0.0.1
EOF
openssl x509 -req -in "$TMP/redis.csr" -CA "$DIR/ca.crt" -CAkey "$DIR/ca.key" -CAcreateserial \
  -days "$DAYS" -sha256 -extfile "$TMP/redis.ext" -out "$DIR/redis.crt"
rm -f "$DIR/ca.srl"

# Certificates are public; private keys stay readable by the owner only.
chmod 644 "$DIR/ca.crt" "$DIR/redis.crt"
chmod 600 "$DIR/ca.key" "$DIR/redis.key"

echo "Created the local dev CA and Redis certificate in $DIR (valid $DAYS days)."
