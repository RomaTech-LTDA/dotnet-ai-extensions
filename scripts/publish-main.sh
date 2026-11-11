#!/bin/bash
set -euo pipefail
exec "$(dirname "$0")/_publish-one.sh" Romatech.Extensions.Ai pack-main.sh
