#!/bin/bash
set -e

SOLUTION_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
CONFIGURATION="${1:-Release}"

echo "========================================="
echo " Building Romatech.Extensions.Ai"
echo " Configuration: $CONFIGURATION"
echo "========================================="

dotnet restore "$SOLUTION_ROOT/Romatech.Extensions.Ai.sln"
dotnet build "$SOLUTION_ROOT/Romatech.Extensions.Ai.sln" -c "$CONFIGURATION" --no-restore

echo ""
echo "✅ Build completed successfully."
