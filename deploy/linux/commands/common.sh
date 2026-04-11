#!/usr/bin/env bash
set -euo pipefail

PAYLOAD_FILE="${1:-}"
if [[ -z "$PAYLOAD_FILE" || ! -f "$PAYLOAD_FILE" ]]; then
  echo "Payload file is required." >&2
  exit 1
fi

FM_ROOT="${FM_ROOT:-/var/lib/fleetmanager}"
FM_SESSIONS_ROOT="${FM_SESSIONS_ROOT:-$FM_ROOT/sessions}"
FM_LOG_ROOT="${FM_LOG_ROOT:-$FM_ROOT/logs}"
FM_VIEWER_HOST="${FM_VIEWER_HOST:-127.0.0.1}"
mkdir -p "$FM_SESSIONS_ROOT" "$FM_LOG_ROOT"

json_get() {
  python3 - "$PAYLOAD_FILE" "$1" <<'PY'
import json, sys
with open(sys.argv[1], 'r', encoding='utf-8') as fh:
    data = json.load(fh)
value = data.get(sys.argv[2], "")
print("" if value is None else value)
PY
}

ACCOUNT_ID="$(json_get accountId)"
EMAIL="$(json_get email)"
USERNAME="$(json_get username)"

if [[ -z "$ACCOUNT_ID" ]]; then
  echo "accountId is required in the payload." >&2
  exit 1
fi

SESSION_DIR="$FM_SESSIONS_ROOT/$ACCOUNT_ID"
SESSION_LOG="$FM_LOG_ROOT/$ACCOUNT_ID.log"
mkdir -p "$SESSION_DIR"

log_line() {
  printf '%s %s\n' "$(date -Iseconds)" "$*" | tee -a "$SESSION_LOG"
}

allocate_viewer_slot() {
  local meta_file="$SESSION_DIR/viewer.env"
  if [[ -f "$meta_file" ]]; then
    # shellcheck disable=SC1090
    source "$meta_file"
    return
  fi

  local lock_dir="$FM_ROOT/viewer-slot.lock"
  until mkdir "$lock_dir" 2>/dev/null; do
    sleep 0.2
  done

  local counter_file="$FM_ROOT/viewer-slot.counter"
  local slot=1
  if [[ -f "$counter_file" ]]; then
    slot="$(( $(cat "$counter_file") + 1 ))"
  fi
  echo "$slot" > "$counter_file"
  rmdir "$lock_dir"

  FM_SLOT="$slot"
  FM_DISPLAY=":$((100 + slot))"
  FM_VNC_PORT="$((5900 + slot))"
  FM_WEB_PORT="$((6900 + slot))"
  FM_DEBUG_PORT="$((9222 + slot))"

  cat > "$meta_file" <<EOF
FM_SLOT=$FM_SLOT
FM_DISPLAY=$FM_DISPLAY
FM_VNC_PORT=$FM_VNC_PORT
FM_WEB_PORT=$FM_WEB_PORT
FM_DEBUG_PORT=$FM_DEBUG_PORT
EOF
}

ensure_viewer_stack() {
  allocate_viewer_slot
  # shellcheck disable=SC1090
  source "$SESSION_DIR/viewer.env"

  if ! command -v Xvfb >/dev/null 2>&1; then
    echo "Xvfb is not installed." >&2
    return 1
  fi

  if ! pgrep -f "Xvfb $FM_DISPLAY" >/dev/null 2>&1; then
    Xvfb "$FM_DISPLAY" -screen 0 1600x900x24 >> "$SESSION_DIR/viewer.log" 2>&1 &
    sleep 1
  fi

  if command -v fluxbox >/dev/null 2>&1 && ! pgrep -f "fluxbox.*$FM_DISPLAY" >/dev/null 2>&1; then
    DISPLAY="$FM_DISPLAY" fluxbox >> "$SESSION_DIR/viewer.log" 2>&1 &
  fi

  if ! command -v x11vnc >/dev/null 2>&1; then
    echo "x11vnc is not installed." >&2
    return 1
  fi

  if ! pgrep -f "x11vnc .* -rfbport $FM_VNC_PORT" >/dev/null 2>&1; then
    DISPLAY="$FM_DISPLAY" x11vnc -forever -shared -nopw -rfbport "$FM_VNC_PORT" >> "$SESSION_DIR/viewer.log" 2>&1 &
  fi

  local novnc_dir="${FM_NOVNC_DIR:-/usr/share/novnc}"
  local websockify_bin="${FM_WEBSOCKIFY_BIN:-$(command -v websockify || true)}"
  if [[ -n "$websockify_bin" && -d "$novnc_dir" ]] && ! pgrep -f "websockify .* $FM_WEB_PORT .* $FM_VNC_PORT" >/dev/null 2>&1; then
    "$websockify_bin" --web "$novnc_dir" "$FM_WEB_PORT" "127.0.0.1:$FM_VNC_PORT" >> "$SESSION_DIR/viewer.log" 2>&1 &
  fi
}

resolve_browser_bin() {
  if [[ -n "${FM_BROWSER_BIN:-}" && -x "${FM_BROWSER_BIN:-}" ]]; then
    printf '%s\n' "$FM_BROWSER_BIN"
    return
  fi

  for candidate in /usr/bin/chromium /usr/bin/chromium-browser /usr/bin/google-chrome /usr/bin/google-chrome-stable; do
    if [[ -x "$candidate" ]]; then
      printf '%s\n' "$candidate"
      return
    fi
  done

  return 1
}

browser_pid_file() {
  printf '%s/browser.pid\n' "$SESSION_DIR"
}

browser_is_running() {
  local pid_file
  pid_file="$(browser_pid_file)"
  [[ -f "$pid_file" ]] || return 1
  local pid
  pid="$(cat "$pid_file")"
  kill -0 "$pid" >/dev/null 2>&1
}
