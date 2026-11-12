#!/usr/bin/env bash
set -euo pipefail

echo "==> Generating Malabak API Endpoint Inventory..."
export GENERATE_ENDPOINT_INVENTORY=1
export DOTNET_ROOT="${DOTNET_ROOT:-/home/osama/.dotnet10}"
export PATH="$DOTNET_ROOT:$PATH"

dotnet test tests/Malabak.IntegrationTests --filter "FullyQualifiedName~EndpointInventoryTests" -p:NoWarn=NU1603
echo "==> Endpoint inventory generated at docs/testing/endpoint-inventory.md"
