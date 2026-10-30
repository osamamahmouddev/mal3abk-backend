#!/bin/sh
# Runs the whole stack locally: infrastructure, API (migrated and seeded), venue, admin and landing.
#   ./scripts/run-local.sh            prepare everything, then run the API and the three web apps
#   ./scripts/run-local.sh --prepare  only prepare (certs, containers, secrets, database, web env files)
# Ctrl+C stops the API and the web apps; the containers keep running (docker compose down to stop them).
set -e

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"

need() {
  command -v "$1" >/dev/null 2>&1 || { echo "Missing $1. $2" >&2; exit 1; }
}
need docker "Install Docker: https://docs.docker.com/get-docker/"
need dotnet "Install the .NET 10 SDK: https://dotnet.microsoft.com/download/dotnet/10.0"
need node "Install Node.js 20+: https://nodejs.org/"
need openssl "Install OpenSSL."
case "$(dotnet --version)" in
  10.*) ;;
  *) echo "The API needs the .NET 10 SDK (found $(dotnet --version))." >&2; exit 1 ;;
esac
PNPM="pnpm"
command -v pnpm >/dev/null 2>&1 && pnpm --version | grep -q '^9\.' || PNPM="npx -y pnpm@9.0.0"

echo "→ Certificates and containers"
./scripts/generate-dev-certs.sh
docker compose up -d --wait postgres redis mailpit seq

echo "→ Local secrets (User Secrets, generated once per machine)"
./scripts/setup-dev-secrets.sh >/dev/null

echo "→ Database: migrations, reference data, demo data, dev account passwords"
dotnet build src/Malabak.Api -v q -nologo >/dev/null
for step in --migrate --seed-reference --seed-demo --seed-dev-accounts; do
  ASPNETCORE_ENVIRONMENT=Development dotnet run --no-build --no-launch-profile --project src/Malabak.Api -- "$step" >/dev/null
done

echo "→ Web apps"
for app in venue admin; do
  env_file="web/apps/$app/.env.local"
  if [ ! -f "$env_file" ]; then
    printf "API_URL=http://localhost:5000\nSESSION_SECRET=%s\n" "$(openssl rand -base64 32)" > "$env_file"
  fi
done
(cd web && COREPACK_ENABLE_STRICT=0 $PNPM install --frozen-lockfile >/dev/null)

if [ "$1" = "--prepare" ]; then
  echo "Ready. Run ./scripts/run-local.sh to start everything."
  exit 0
fi

PIDS=""
cleanup() {
  [ -n "$PIDS" ] && kill $PIDS 2>/dev/null || true
}
trap cleanup INT TERM EXIT

ASPNETCORE_ENVIRONMENT=Development dotnet run --no-build --no-launch-profile --project src/Malabak.Api -- --urls http://localhost:5000 &
PIDS="$PIDS $!"
(cd web && COREPACK_ENABLE_STRICT=0 $PNPM --filter @malabak/venue-dashboard --filter @malabak/admin-dashboard --filter @malabak/landing --parallel dev) &
PIDS="$PIDS $!"

cat <<EOF

  API + Swagger   http://localhost:5000/swagger
  Venue           http://localhost:3000   owner.ahly@malabak.app
  Admin           http://localhost:3001   admin@malabak.app (enroll 2FA on first sign-in)
  Landing         http://localhost:3002
  Mailpit         http://localhost:8025   (verification and reset emails)
  Seq logs        http://localhost:5341

  Dev account password:
    dotnet user-secrets list --project src/Malabak.Api | grep Seed:DevAccountPassword

EOF
wait
