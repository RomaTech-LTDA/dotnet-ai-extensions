#!/bin/bash
set -euo pipefail
exec "$(dirname "$0")/_publish-one.sh" Romatech.Extensions.Ai.Rag pack-rag.sh
