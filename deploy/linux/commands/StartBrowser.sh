#!/usr/bin/env bash
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/common.sh" "$1"

ensure_viewer_stack

if browser_is_running; then
  log_line "Browser already running for $ACCOUNT_ID"
  echo "Browser already running."
  exit 0
fi

BROWSER_BIN="$(resolve_browser_bin || true)"
if [[ -z "$BROWSER_BIN" ]]; then
  echo "No Chromium/Chrome binary found. Set FM_BROWSER_BIN in the systemd service." >&2
  exit 1
fi

source "$SESSION_DIR/viewer.env"
mkdir -p "$SESSION_DIR/profile"
DISPLAY="$FM_DISPLAY" "$BROWSER_BIN" \
  --user-data-dir="$SESSION_DIR/profile" \
  --remote-debugging-port="$FM_DEBUG_PORT" \
  --no-first-run \
  --no-default-browser-check \
  about:blank >> "$SESSION_DIR/browser.log" 2>&1 &
echo $! > "$(browser_pid_file)"

log_line "Started browser for $ACCOUNT_ID on display $FM_DISPLAY and debug port $FM_DEBUG_PORT"
echo "Browser started on $FM_DISPLAY. Viewer port: $FM_WEB_PORT"
