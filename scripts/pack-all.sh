#!/bin/bash
set -e

SOLUTION_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
CONFIGURATION="${1:-Release}"
OUTPUT_DIR="$SOLUTION_ROOT/artifacts"
VERSION="${PACKAGE_VERSION:-}"

echo "========================================="
echo " Packing ALL NuGet Packages"
echo " Configuration: $CONFIGURATION"
echo " Output: $OUTPUT_DIR"
[ -n "$VERSION" ] && echo " Version: $VERSION"
echo "========================================="

rm -rf "$OUTPUT_DIR"
mkdir -p "$OUTPUT_DIR"

VERSION_ARG=""
if [ -n "$VERSION" ]; then
  VERSION_ARG="/p:Version=$VERSION"
fi

dotnet pack "$SOLUTION_ROOT/src/Romatech.Extensions.Ai.Shared/Romatech.Extensions.Ai.Shared.csproj" \
  -c "$CONFIGURATION" --no-build --output "$OUTPUT_DIR" $VERSION_ARG

dotnet pack "$SOLUTION_ROOT/src/Romatech.Extensions.Ai.Metadata/Romatech.Extensions.Ai.Metadata.csproj" \
  -c "$CONFIGURATION" --no-build --output "$OUTPUT_DIR" $VERSION_ARG

dotnet pack "$SOLUTION_ROOT/src/Romatech.Extensions.Ai.Swagger/Romatech.Extensions.Ai.Swagger.csproj" \
  -c "$CONFIGURATION" --no-build --output "$OUTPUT_DIR" $VERSION_ARG

dotnet pack "$SOLUTION_ROOT/src/Romatech.Extensions.Ai.Mcp/Romatech.Extensions.Ai.Mcp.csproj" \
  -c "$CONFIGURATION" --no-build --output "$OUTPUT_DIR" $VERSION_ARG

dotnet pack "$SOLUTION_ROOT/src/Romatech.Extensions.Ai.Rag/Romatech.Extensions.Ai.Rag.csproj" \
  -c "$CONFIGURATION" --no-build --output "$OUTPUT_DIR" $VERSION_ARG

dotnet pack "$SOLUTION_ROOT/src/Romatech.Extensions.Ai/Romatech.Extensions.Ai.csproj" \
  -c "$CONFIGURATION" --no-build --output "$OUTPUT_DIR" $VERSION_ARG

echo ""
echo "✅ All packages created in: $OUTPUT_DIR"
echo ""
ls -la "$OUTPUT_DIR"/*.nupkg
