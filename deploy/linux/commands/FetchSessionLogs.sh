#!/usr/bin/env bash
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/common.sh" "$1"

touch "$SESSION_LOG"
echo "Log file: $SESSION_LOG"
tail -n 50 "$SESSION_LOG" || true
