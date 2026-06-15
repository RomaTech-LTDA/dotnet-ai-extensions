#!/bin/bash
set -e

SOLUTION_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
CONFIGURATION="${1:-Release}"
OUTPUT_DIR="$SOLUTION_ROOT/artifacts"
VERSION="${PACKAGE_VERSION:-}"

echo "========================================="
echo " Packing: Romatech.Extensions.Ai.Metadata"
echo "========================================="

mkdir -p "$OUTPUT_DIR"

VERSION_ARG=""
if [ -n "$VERSION" ]; then
  VERSION_ARG="/p:Version=$VERSION"
fi

dotnet build "$SOLUTION_ROOT/src/Romatech.Extensions.Ai.Metadata/Romatech.Extensions.Ai.Metadata.csproj" -c "$CONFIGURATION"
dotnet pack "$SOLUTION_ROOT/src/Romatech.Extensions.Ai.Metadata/Romatech.Extensions.Ai.Metadata.csproj" \
  -c "$CONFIGURATION" --no-build --output "$OUTPUT_DIR" $VERSION_ARG

echo "✅ Package created in: $OUTPUT_DIR"
