#!/bin/bash
# Packs the current version into a clean folder and publishes every package of that version.
# The official release path is .github/workflows/publish.yml; this script is for manual recovery only.
set -euo pipefail

SOLUTION_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUTPUT_DIR="$SOLUTION_ROOT/artifacts"
NUGET_SOURCE="${NUGET_SOURCE:-https://api.nuget.org/v3/index.json}"

if [ -z "${NUGET_API_KEY:-}" ]; then
  echo "Error: NUGET_API_KEY environment variable is required."
  echo "Usage: NUGET_API_KEY=your-key ./scripts/publish-all.sh"
  exit 1
fi

VERSION="${PACKAGE_VERSION:-$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$SOLUTION_ROOT/Directory.Build.props" | head -1)}"

# Never push stale packages left from earlier builds.
rm -rf "$OUTPUT_DIR"
"$SOLUTION_ROOT/scripts/build.sh"
"$SOLUTION_ROOT/scripts/pack-all.sh"

shopt -s nullglob
packages=("$OUTPUT_DIR"/*."$VERSION".nupkg)
if [ ${#packages[@]} -eq 0 ]; then
  echo "Error: no packages for version $VERSION in $OUTPUT_DIR"
  exit 1
fi

for pkg in "${packages[@]}"; do
  echo "Publishing $(basename "$pkg")"
  dotnet nuget push "$pkg" --api-key "$NUGET_API_KEY" --source "$NUGET_SOURCE" --skip-duplicate
done

echo "All packages of version $VERSION published."
