#!/bin/bash
set -e

SOLUTION_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUTPUT_DIR="$SOLUTION_ROOT/artifacts"
NUGET_SOURCE="${NUGET_SOURCE:-https://api.nuget.org/v3/index.json}"

if [ -z "$NUGET_API_KEY" ]; then
  echo "❌ Error: NUGET_API_KEY environment variable is required."
  echo ""
  echo "Usage:"
  echo "  NUGET_API_KEY=your-key ./scripts/publish-all.sh"
  echo ""
  echo "Optional:"
  echo "  NUGET_SOURCE=https://custom-feed/index.json"
  exit 1
fi

echo "========================================="
echo " Publishing ALL NuGet Packages"
echo " Source: $NUGET_SOURCE"
echo " Packages: $OUTPUT_DIR"
echo "========================================="

if [ ! -d "$OUTPUT_DIR" ] || [ -z "$(ls -A "$OUTPUT_DIR"/*.nupkg 2>/dev/null)" ]; then
  echo "⚠️  No packages found. Running build + pack first..."
  "$SOLUTION_ROOT/scripts/build.sh"
  "$SOLUTION_ROOT/scripts/pack-all.sh"
fi

for pkg in "$OUTPUT_DIR"/*.nupkg; do
  echo ""
  echo "📦 Publishing: $(basename "$pkg")"
  dotnet nuget push "$pkg" \
    --api-key "$NUGET_API_KEY" \
    --source "$NUGET_SOURCE" \
    --skip-duplicate
done

echo ""
echo "✅ All packages published successfully."
