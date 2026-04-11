#!/usr/bin/env bash
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/common.sh" "$1"

source "$SESSION_DIR/viewer.env" 2>/dev/null || true
log_line "Bring window to front requested for $ACCOUNT_ID"
echo "Bring-to-front requested. Use DISPLAY ${FM_DISPLAY:-unknown}."
