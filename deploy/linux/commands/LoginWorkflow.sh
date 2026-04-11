#!/usr/bin/env bash
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/common.sh" "$1"

log_line "Login workflow requested for $ACCOUNT_ID ($EMAIL)"
echo "Login workflow marked for account $ACCOUNT_ID."
