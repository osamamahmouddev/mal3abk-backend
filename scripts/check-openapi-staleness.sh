#!/usr/bin/env bash
set -euo pipefail

echo "=================================================="
echo "  Checking OpenAPI Contract & Client Staleness"
echo "=================================================="

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"

# Ensure dotnet is available
if [ -d "$HOME/dotnet10" ]; then
    export PATH="$HOME/dotnet10:$PATH"
    export DOTNET_ROOT="$HOME/dotnet10"
else
    export PATH="$HOME/.dotnet:$PATH"
fi

CONTRACT_FILE="docs/openapi/v1.json"

if [ ! -f "$CONTRACT_FILE" ]; then
    echo "ERROR: $CONTRACT_FILE does not exist. Generating contract..."
    dotnet test tests/Malabak.IntegrationTests/Malabak.IntegrationTests.csproj --filter OpenApiContractIntegrationTests
fi

echo "Running OpenAPI contract verification test..."
dotnet test tests/Malabak.IntegrationTests/Malabak.IntegrationTests.csproj --filter OpenApiContractIntegrationTests --verbosity minimal

# Check for unstaged git changes in OpenAPI contract
if ! git diff --exit-code "$CONTRACT_FILE" > /dev/null 2>&1; then
    echo "❌ ERROR: docs/openapi/v1.json is out of date with API controllers / endpoints!"
    echo "Run the integration tests locally to sync docs/openapi/v1.json and commit changes."
    git diff "$CONTRACT_FILE"
    exit 1
fi

echo "✅ OpenAPI contract is up to date with backend definitions."
