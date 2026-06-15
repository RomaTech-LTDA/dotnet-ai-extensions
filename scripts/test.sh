#!/bin/bash
set -e

SOLUTION_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
CONFIGURATION="${1:-Release}"

echo "========================================="
echo " Running Tests"
echo " Configuration: $CONFIGURATION"
echo "========================================="

dotnet test "$SOLUTION_ROOT/tests/Romatech.Extensions.Ai.Tests" \
  -c "$CONFIGURATION" \
  --verbosity normal \
  --collect:"XPlat Code Coverage"

echo ""
echo "✅ Tests completed."
