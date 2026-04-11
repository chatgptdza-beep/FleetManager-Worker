#!/usr/bin/env bash
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/common.sh" "$1"

ensure_viewer_stack
source "$SESSION_DIR/viewer.env"

if ! browser_is_running; then
  "$SCRIPT_DIR/StartBrowser.sh" "$1" >/dev/null
fi

log_line "Viewer opened for $ACCOUNT_ID"
echo "Viewer ready. URL: http://$FM_VIEWER_HOST:$FM_WEB_PORT/vnc.html?host=$FM_VIEWER_HOST&port=$FM_WEB_PORT&autoconnect=true&resize=scale&show_dot=true"
echo "Debug port: $FM_DEBUG_PORT"
