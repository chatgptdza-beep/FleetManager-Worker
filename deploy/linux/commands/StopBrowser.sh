#!/usr/bin/env bash
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/common.sh" "$1"

if browser_is_running; then
  pid="$(cat "$(browser_pid_file)")"
  kill "$pid"
  rm -f "$(browser_pid_file)"
  log_line "Stopped browser for $ACCOUNT_ID"
  echo "Browser stopped."
  exit 0
fi

echo "Browser is not running."
