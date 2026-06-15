#!/bin/bash
set -e

SOLUTION_ROOT="$(cd "$(dirname "$0")/.." && pwd)"

echo "========================================="
echo " CI Pipeline: Build → Test → Pack"
echo "========================================="

echo ""
echo "--- [1/3] Build ---"
"$SOLUTION_ROOT/scripts/build.sh"

echo ""
echo "--- [2/3] Test ---"
"$SOLUTION_ROOT/scripts/test.sh"

echo ""
echo "--- [3/3] Pack ---"
"$SOLUTION_ROOT/scripts/pack-all.sh"

echo ""
echo "========================================="
echo " ✅ CI Pipeline completed successfully"
echo "========================================="
