#!/bin/bash
set -e

SOLUTION_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUTPUT_DIR="$SOLUTION_ROOT/artifacts"
NUGET_SOURCE="${NUGET_SOURCE:-https://api.nuget.org/v3/index.json}"
PACKAGE_NAME="Romatech.Extensions.Ai.Swagger"

if [ -z "$NUGET_API_KEY" ]; then
  echo "❌ Error: NUGET_API_KEY environment variable is required."
  exit 1
fi

echo "========================================="
echo " Publishing: $PACKAGE_NAME"
echo " Source: $NUGET_SOURCE"
echo "========================================="

PKG=$(find "$OUTPUT_DIR" -name "${PACKAGE_NAME}.*.nupkg" | sort -V | tail -1)

if [ -z "$PKG" ]; then
  echo "⚠️  Package not found. Running pack first..."
  "$SOLUTION_ROOT/scripts/pack-swagger.sh"
  PKG=$(find "$OUTPUT_DIR" -name "${PACKAGE_NAME}.*.nupkg" | sort -V | tail -1)
fi

echo "📦 Publishing: $(basename "$PKG")"
dotnet nuget push "$PKG" \
  --api-key "$NUGET_API_KEY" \
  --source "$NUGET_SOURCE" \
  --skip-duplicate

echo "✅ $PACKAGE_NAME published."
