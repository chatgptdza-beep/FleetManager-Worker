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
BROWSER_EXTENSION_LIST=""

resolve_extension_root() {
  local candidate="$1"

  if [[ -f "$candidate/manifest.json" ]]; then
    printf '%s\n' "$candidate"
    return 0
  fi

  local nested_manifest
  nested_manifest="$(find "$candidate" -mindepth 2 -maxdepth 2 -type f -name manifest.json 2>/dev/null | head -n 1 || true)"
  if [[ -n "$nested_manifest" ]]; then
    printf '%s\n' "$(dirname "$nested_manifest")"
    return 0
  fi

  return 1
}

if [[ -n "${FM_BROWSER_EXTENSIONS:-}" ]]; then
  IFS=',' read -r -a EXTENSION_CANDIDATES <<< "$FM_BROWSER_EXTENSIONS"
  VALID_EXTENSIONS=()
  STAGED_EXTENSIONS_DIR="$PROFILE_ROOT/extensions"
  mkdir -p "$STAGED_EXTENSIONS_DIR"

  EXTENSION_INDEX=0
  for extension_path in "${EXTENSION_CANDIDATES[@]}"; do
    extension_path="${extension_path#"${extension_path%%[![:space:]]*}"}"
    extension_path="${extension_path%"${extension_path##*[![:space:]]}"}"

    if [[ -z "$extension_path" ]]; then
      continue
    fi

    if [[ ! -d "$extension_path" ]]; then
      log_line "Skipping missing extension directory: $extension_path"
      continue
    fi

    EXTENSION_ROOT=""
    if ! EXTENSION_ROOT="$(resolve_extension_root "$extension_path")"; then
      log_line "Skipping extension without manifest.json: $extension_path"
      continue
    fi

    STAGED_EXTENSION_PATH="$STAGED_EXTENSIONS_DIR/ext-$EXTENSION_INDEX"
    rm -rf "$STAGED_EXTENSION_PATH"
    cp -a "$EXTENSION_ROOT" "$STAGED_EXTENSION_PATH"

    if id -u "$BROWSER_USER" >/dev/null 2>&1; then
      chown -R "$BROWSER_USER":"$BROWSER_USER" "$STAGED_EXTENSION_PATH" || true
    fi

    VALID_EXTENSIONS+=("$STAGED_EXTENSION_PATH")
    EXTENSION_INDEX=$((EXTENSION_INDEX + 1))
  done

  if (( ${#VALID_EXTENSIONS[@]} > 0 )); then
    BROWSER_EXTENSION_LIST="$(IFS=,; echo "${VALID_EXTENSIONS[*]}")"
    log_line "Loading ${#VALID_EXTENSIONS[@]} static extension(s) from staged unpacked folders."
  fi
fi

mkdir -p "$PROFILE_DIR"
rm -f "$PROFILE_DIR"/SingletonLock \
      "$PROFILE_DIR"/SingletonCookie \
      "$PROFILE_DIR"/SingletonSocket || true
rm -f "$PROFILE_ROOT"/SingletonLock \
      "$PROFILE_ROOT"/SingletonCookie \
      "$PROFILE_ROOT"/SingletonSocket || true

BROWSER_ARGS=(
  "--user-data-dir=$PROFILE_DIR"
  "--remote-debugging-port=$FM_DEBUG_PORT"
  --no-sandbox
  --disable-setuid-sandbox
  --disable-dev-shm-usage
  --disable-gpu
  --ozone-platform=x11
  --window-size=1600,900
  --window-position=0,0
  --start-maximized
  --no-first-run
  --no-default-browser-check
)

if [[ -n "$BROWSER_EXTENSION_LIST" ]]; then
  BROWSER_ARGS+=("--disable-extensions-except=$BROWSER_EXTENSION_LIST")
  BROWSER_ARGS+=("--load-extension=$BROWSER_EXTENSION_LIST")
fi

BROWSER_ARGS+=("chrome://newtab")

if id -u "$BROWSER_USER" >/dev/null 2>&1; then
  BROWSER_UID="$(id -u "$BROWSER_USER")"
  BROWSER_GID="$(id -g "$BROWSER_USER")"
  BROWSER_HOME="$(getent passwd "$BROWSER_USER" | cut -d: -f6)"
  XDG_RUNTIME_DIR="/run/user/$BROWSER_UID"
  install -d -m 700 -o "$BROWSER_USER" -g "$BROWSER_USER" "$PROFILE_ROOT" "$PROFILE_DIR" "$XDG_CONFIG_DIR" "$XDG_CACHE_DIR" "$XDG_RUNTIME_DIR"
  runuser -u "$BROWSER_USER" -- env DISPLAY="$FM_DISPLAY" HOME="$BROWSER_HOME" XDG_RUNTIME_DIR="$XDG_RUNTIME_DIR" XDG_CONFIG_HOME="$XDG_CONFIG_DIR" XDG_CACHE_HOME="$XDG_CACHE_DIR" \
    "$BROWSER_BIN" "${BROWSER_ARGS[@]}" >> "$SESSION_DIR/browser.log" 2>&1 &
else
  mkdir -p "$XDG_CONFIG_DIR" "$XDG_CACHE_DIR"
  chmod 700 "$PROFILE_ROOT" "$PROFILE_DIR" "$XDG_CONFIG_DIR" "$XDG_CACHE_DIR" || true
  DISPLAY="$FM_DISPLAY" HOME="/root" XDG_CONFIG_HOME="$XDG_CONFIG_DIR" XDG_CACHE_HOME="$XDG_CACHE_DIR" "$BROWSER_BIN" \
    "${BROWSER_ARGS[@]}" >> "$SESSION_DIR/browser.log" 2>&1 &
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
