#!/bin/bash
# Publishes exactly one package at exactly the version being released.
# Usage: _publish-one.sh <PackageId> <pack-script>
# The official release path is .github/workflows/publish.yml; this script is for manual recovery only.
set -euo pipefail

PACKAGE_NAME="$1"
PACK_SCRIPT="$2"
SOLUTION_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUTPUT_DIR="$SOLUTION_ROOT/artifacts"
NUGET_SOURCE="${NUGET_SOURCE:-https://api.nuget.org/v3/index.json}"

if [ -z "${NUGET_API_KEY:-}" ]; then
  echo "Error: NUGET_API_KEY environment variable is required."
  exit 1
fi

VERSION="${PACKAGE_VERSION:-$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$SOLUTION_ROOT/Directory.Build.props" | head -1)}"
if [ -z "$VERSION" ]; then
  echo "Error: could not determine the package version."
  exit 1
fi

PKG="$OUTPUT_DIR/${PACKAGE_NAME}.${VERSION}.nupkg"
[ -f "$PKG" ] || "$SOLUTION_ROOT/scripts/$PACK_SCRIPT"
[ -f "$PKG" ] || { echo "Error: $PKG not found after packing."; exit 1; }

echo "Publishing $(basename "$PKG") to $NUGET_SOURCE"
# The symbols package (.snupkg) next to it is pushed automatically.
dotnet nuget push "$PKG" --api-key "$NUGET_API_KEY" --source "$NUGET_SOURCE" --skip-duplicate
echo "$PACKAGE_NAME $VERSION published."
