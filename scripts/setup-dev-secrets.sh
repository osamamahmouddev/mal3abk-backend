#!/bin/sh
# Stores local development credentials in .NET User Secrets (never in the repo — see SECURITY.md).
# Values match the local containers defined in docker-compose.yml. Run once per machine, after
# ./scripts/generate-dev-certs.sh (Redis only accepts TLS):
#   ./scripts/setup-dev-secrets.sh
set -e

PROJECT=src/Malabak.Api
REDIS_CA="$(cd "$(dirname "$0")/.." && pwd)/docker/redis/tls/ca.crt"
if [ ! -f "$REDIS_CA" ]; then
  echo "Missing $REDIS_CA — run ./scripts/generate-dev-certs.sh first." >&2
  exit 1
fi
PG="Host=localhost;Port=5432;Database=malabak_dev;Username=malabak;Password=malabak_dev_password"
REDIS="localhost:6379,password=malabak_dev_redis_password,ssl=true,abortConnect=false"

dotnet user-secrets set "ConnectionStrings:Postgres" "$PG" --project "$PROJECT"
dotnet user-secrets set "ConnectionStrings:Redis" "$REDIS" --project "$PROJECT"
dotnet user-secrets set "Database:ConnectionString" "$PG" --project "$PROJECT"
dotnet user-secrets set "Redis:ConnectionString" "$REDIS" --project "$PROJECT"
dotnet user-secrets set "Redis:TlsCaCertificatePath" "$REDIS_CA" --project "$PROJECT"
# Cloudflare Turnstile public test keys (always pass) — local only.
dotnet user-secrets set "Security:Turnstile:SecretKey" "1x0000000000000000000000000000000AA" --project "$PROJECT"
dotnet user-secrets set "Security:Turnstile:SiteKey" "1x00000000000000000000AA" --project "$PROJECT"

# Field-encryption keys (AES-256 KEK + HMAC blind-index key). Random per machine and generated once, so
# data encrypted locally (TOTP secrets, payment details, data-protection keys) survives API restarts.
if ! dotnet user-secrets list --project "$PROJECT" | grep -q "^Security:Encryption:CurrentKeyEncryptionKeyId"; then
  dotnet user-secrets set "Security:Encryption:CurrentKeyEncryptionKeyId" "dev-kek-1" --project "$PROJECT"
  dotnet user-secrets set "Security:Encryption:KeyEncryptionKeys:dev-kek-1" "$(openssl rand -base64 32)" --project "$PROJECT"
  dotnet user-secrets set "Security:Encryption:BlindIndexKey" "$(openssl rand -base64 32)" --project "$PROJECT"
fi

# Password for the seeded admin and demo owner (./scripts/run-local.sh applies it). Random per machine.
if ! dotnet user-secrets list --project "$PROJECT" | grep -q "^Seed:DevAccountPassword"; then
  dotnet user-secrets set "Seed:DevAccountPassword" "$(openssl rand -base64 18)" --project "$PROJECT"
fi

echo "Local development secrets stored in User Secrets for $PROJECT."
echo "Dev account password: dotnet user-secrets list --project $PROJECT | grep Seed:DevAccountPassword"
