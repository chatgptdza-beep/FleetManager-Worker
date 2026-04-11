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
PROFILE_ROOT="$SESSION_DIR/profile"
PROFILE_DIR="$PROFILE_ROOT/chrome-data"
BROWSER_USER="${FM_BROWSER_USER:-fmview}"
XDG_CONFIG_DIR="$PROFILE_ROOT/.config"
XDG_CACHE_DIR="$PROFILE_ROOT/.cache"
mkdir -p "$PROFILE_DIR"
rm -f "$PROFILE_DIR"/SingletonLock \
      "$PROFILE_DIR"/SingletonCookie \
      "$PROFILE_DIR"/SingletonSocket || true
rm -f "$PROFILE_ROOT"/SingletonLock \
      "$PROFILE_ROOT"/SingletonCookie \
      "$PROFILE_ROOT"/SingletonSocket || true

if id -u "$BROWSER_USER" >/dev/null 2>&1; then
  BROWSER_UID="$(id -u "$BROWSER_USER")"
  BROWSER_GID="$(id -g "$BROWSER_USER")"
  BROWSER_HOME="$(getent passwd "$BROWSER_USER" | cut -d: -f6)"
  XDG_RUNTIME_DIR="/run/user/$BROWSER_UID"
  install -d -m 700 -o "$BROWSER_USER" -g "$BROWSER_USER" "$PROFILE_ROOT" "$PROFILE_DIR" "$XDG_CONFIG_DIR" "$XDG_CACHE_DIR" "$XDG_RUNTIME_DIR"
  runuser -u "$BROWSER_USER" -- env DISPLAY="$FM_DISPLAY" HOME="$BROWSER_HOME" XDG_RUNTIME_DIR="$XDG_RUNTIME_DIR" XDG_CONFIG_HOME="$XDG_CONFIG_DIR" XDG_CACHE_HOME="$XDG_CACHE_DIR" \
    "$BROWSER_BIN" \
    --user-data-dir="$PROFILE_DIR" \
    --remote-debugging-port="$FM_DEBUG_PORT" \
    --no-sandbox \
    --disable-setuid-sandbox \
    --disable-dev-shm-usage \
    --disable-gpu \
    --ozone-platform=x11 \
    --window-size=1600,900 \
    --window-position=0,0 \
    --start-maximized \
    --no-first-run \
    --no-default-browser-check \
    chrome://newtab >> "$SESSION_DIR/browser.log" 2>&1 &
else
  chmod 700 "$PROFILE_ROOT" "$PROFILE_DIR" "$XDG_CONFIG_DIR" "$XDG_CACHE_DIR" || true
  DISPLAY="$FM_DISPLAY" HOME="/root" XDG_CONFIG_HOME="$XDG_CONFIG_DIR" XDG_CACHE_HOME="$XDG_CACHE_DIR" "$BROWSER_BIN" \
    --user-data-dir="$PROFILE_DIR" \
    --remote-debugging-port="$FM_DEBUG_PORT" \
    --no-sandbox \
    --disable-setuid-sandbox \
    --disable-dev-shm-usage \
    --disable-gpu \
    --ozone-platform=x11 \
    --window-size=1600,900 \
    --window-position=0,0 \
    --start-maximized \
    --no-first-run \
    --no-default-browser-check \
    chrome://newtab >> "$SESSION_DIR/browser.log" 2>&1 &
fi

sleep 1
BROWSER_PID="$(pgrep -f "$PROFILE_DIR" | head -n 1 || true)"
if [[ -z "$BROWSER_PID" ]]; then
  echo "Browser failed to start. Check $SESSION_DIR/browser.log" >&2
  exit 1
fi
echo "$BROWSER_PID" > "$(browser_pid_file)"

log_line "Started browser for $ACCOUNT_ID on display $FM_DISPLAY and debug port $FM_DEBUG_PORT"
echo "Browser started on $FM_DISPLAY. Viewer port: $FM_WEB_PORT"
