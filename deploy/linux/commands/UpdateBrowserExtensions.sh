#!/usr/bin/env bash
set -euo pipefail

PAYLOAD_FILE="${1:-}"
if [[ -z "$PAYLOAD_FILE" || ! -f "$PAYLOAD_FILE" ]]; then
  echo "Payload file is required." >&2
  exit 1
fi

json_get() {
  python3 - "$PAYLOAD_FILE" "$1" <<'PY'
import json, sys
with open(sys.argv[1], 'r', encoding='utf-8') as fh:
    data = json.load(fh)
value = data.get(sys.argv[2], "")
print("" if value is None else value)
PY
}

BUNDLE_URL="$(json_get bundleUrl)"
BUNDLE_SHA256_URL="$(json_get bundleSha256Url)"
BUNDLE_SHA256="$(json_get bundleSha256)"
INSTALL_PATH="$(json_get installPath)"
RESTART_DELAY_SECONDS="$(json_get restartDelaySeconds)"
DISPLAY_NAME="$(json_get displayName)"
VERSION_NAME="$(json_get version)"
INLINE_BUNDLE_PRESENT="$(python3 - "$PAYLOAD_FILE" <<'PY'
import json, sys
with open(sys.argv[1], 'r', encoding='utf-8') as fh:
    data = json.load(fh)
print("1" if data.get("bundleBase64") else "0")
PY
)"

if [[ "$INLINE_BUNDLE_PRESENT" != "1" && -z "$BUNDLE_URL" ]]; then
  echo "Either bundleBase64 or bundleUrl is required." >&2
  exit 1
fi

if [[ -z "$INSTALL_PATH" ]]; then
  INSTALL_PATH="/opt/fleetmanager-agent/extensions/fleet-managed-extension"
fi

if [[ -z "$RESTART_DELAY_SECONDS" ]]; then
  RESTART_DELAY_SECONDS="6"
fi

INSTALL_DIR="/opt/fleetmanager-agent"
APPSETTINGS_PATH="$INSTALL_DIR/appsettings.json"
SYSTEMD_OVERRIDE_DIR="/etc/systemd/system/fleetmanager-agent.service.d"
SYSTEMD_OVERRIDE_PATH="$SYSTEMD_OVERRIDE_DIR/10-browser-extensions.conf"
TMP_ROOT="${TMPDIR:-/tmp}/fleetmanager-browser-extension-update"
WORK_DIR="$TMP_ROOT/$(date +%s)-$$"
AGENT_PARENT_PID="$PPID"

mkdir -p "$WORK_DIR"
BUNDLE_PATH="$WORK_DIR/browser-extension-bundle.zip"
EXTRACT_DIR="$WORK_DIR/extracted"

cleanup() {
  rm -rf "$WORK_DIR" 2>/dev/null || true
}
trap cleanup EXIT

if [[ "$INLINE_BUNDLE_PRESENT" == "1" ]]; then
  python3 - "$PAYLOAD_FILE" "$BUNDLE_PATH" <<'PY'
import base64, json, sys
with open(sys.argv[1], 'r', encoding='utf-8') as fh:
    data = json.load(fh)
bundle = data.get("bundleBase64", "")
if not bundle:
    raise SystemExit("bundleBase64 is required.")
with open(sys.argv[2], 'wb') as fh:
    fh.write(base64.b64decode(bundle))
PY
else
  curl -fL "$BUNDLE_URL" -o "$BUNDLE_PATH"
fi

if [[ -n "$BUNDLE_SHA256" ]]; then
  printf '%s  %s\n' "$BUNDLE_SHA256" "$BUNDLE_PATH" | sha256sum -c -
elif [[ -n "$BUNDLE_SHA256_URL" ]]; then
  SHA_PATH="$WORK_DIR/browser-extension-bundle.zip.sha256"
  curl -fL "$BUNDLE_SHA256_URL" -o "$SHA_PATH"
  (
    cd "$WORK_DIR"
    sha256sum -c "$(basename "$SHA_PATH")"
  )
else
  echo "No checksum provided. Continuing without SHA256 verification."
fi

mkdir -p "$EXTRACT_DIR"
unzip -oq "$BUNDLE_PATH" -d "$EXTRACT_DIR"

if [[ ! -f "$EXTRACT_DIR/extension/manifest.json" ]]; then
  echo "Bundle does not contain extension/manifest.json." >&2
  exit 1
fi

mkdir -p "$(dirname "$INSTALL_PATH")"
rm -rf "$INSTALL_PATH"
cp -a "$EXTRACT_DIR/extension" "$INSTALL_PATH"
chown -R fleetmgr:fleetmgr "$INSTALL_PATH" || true
find "$INSTALL_PATH" -type d -exec chmod 755 {} \;
find "$INSTALL_PATH" -type f -exec chmod 644 {} \;

python3 - "$APPSETTINGS_PATH" "$INSTALL_PATH" <<'PY'
import json, sys
path, install_path = sys.argv[1], sys.argv[2]
with open(path, 'r', encoding='utf-8') as fh:
    data = json.load(fh)
agent = data.setdefault("Agent", {})
agent["BrowserExtensions"] = [install_path]
with open(path, 'w', encoding='utf-8') as fh:
    json.dump(data, fh, indent=2)
    fh.write("\n")
PY

mkdir -p "$SYSTEMD_OVERRIDE_DIR"
printf '[Service]\nEnvironment=FM_BROWSER_EXTENSIONS=%s\n' "$INSTALL_PATH" > "$SYSTEMD_OVERRIDE_PATH"

nohup bash -lc "sleep $RESTART_DELAY_SECONDS; systemctl daemon-reload; kill -TERM $AGENT_PARENT_PID >/dev/null 2>&1 || true" >/dev/null 2>&1 &

if [[ -n "$DISPLAY_NAME" ]]; then
  if [[ -n "$VERSION_NAME" ]]; then
    echo "Browser extension updated: $DISPLAY_NAME $VERSION_NAME"
  else
    echo "Browser extension updated: $DISPLAY_NAME"
  fi
fi
echo "Installed extension path: $INSTALL_PATH"
echo "Agent restart scheduled in $RESTART_DELAY_SECONDS seconds."
echo "Running browser sessions will use the new extension on their next browser start."
