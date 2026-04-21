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

INSTALL_DIR="/opt/fleetmanager-agent"
COMMANDS_DIR="$INSTALL_DIR/commands"
RUNNER_TEMPLATE_RELATIVE_PATH="deploy/linux/run-agent.template.sh"
APPSETTINGS_PATH="$INSTALL_DIR/appsettings.json"
TMP_ROOT="${TMPDIR:-/tmp}/fleetmanager-agent-update"
WORK_DIR="$TMP_ROOT/$(date +%s)-$$"
BUNDLE_URL="$(json_get bundleUrl)"
BUNDLE_SHA256_URL="$(json_get bundleSha256Url)"
BUNDLE_SHA256="$(json_get bundleSha256)"
RESTART_DELAY_SECONDS="$(json_get restartDelaySeconds)"
AGENT_PARENT_PID="$PPID"

if [[ -z "$BUNDLE_URL" ]]; then
  echo "bundleUrl is required." >&2
  exit 1
fi

if [[ -z "$RESTART_DELAY_SECONDS" ]]; then
  RESTART_DELAY_SECONDS="8"
fi

mkdir -p "$WORK_DIR"
BUNDLE_PATH="$WORK_DIR/agent-bundle.zip"
EXTRACT_DIR="$WORK_DIR/extracted"

cleanup() {
  rm -rf "$WORK_DIR" 2>/dev/null || true
}
trap cleanup EXIT

curl -fL "$BUNDLE_URL" -o "$BUNDLE_PATH"

if [[ -n "$BUNDLE_SHA256" ]]; then
  printf '%s  %s\n' "$BUNDLE_SHA256" "$BUNDLE_PATH" | sha256sum -c -
elif [[ -n "$BUNDLE_SHA256_URL" ]]; then
  SHA_PATH="$WORK_DIR/agent-bundle.zip.sha256"
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

if [[ ! -d "$EXTRACT_DIR/agent" ]]; then
  echo "Bundle does not contain the agent directory." >&2
  exit 1
fi

if [[ ! -d "$EXTRACT_DIR/deploy/linux/commands" ]]; then
  echo "Bundle does not contain updated command scripts." >&2
  exit 1
fi

APPSETTINGS_BACKUP=""
if [[ -f "$APPSETTINGS_PATH" ]]; then
  APPSETTINGS_BACKUP="$WORK_DIR/appsettings.json.backup"
  cp "$APPSETTINGS_PATH" "$APPSETTINGS_BACKUP"
fi

mkdir -p "$COMMANDS_DIR"
cp -a "$EXTRACT_DIR/agent/." "$INSTALL_DIR/"
cp -a "$EXTRACT_DIR/deploy/linux/commands/." "$COMMANDS_DIR/"

RUNNER_TEMPLATE_PATH="$EXTRACT_DIR/$RUNNER_TEMPLATE_RELATIVE_PATH"
if [[ -f "$RUNNER_TEMPLATE_PATH" ]]; then
  cp "$RUNNER_TEMPLATE_PATH" "$INSTALL_DIR/run-agent.sh"
fi

if [[ -n "$APPSETTINGS_BACKUP" && -f "$APPSETTINGS_BACKUP" ]]; then
  cp "$APPSETTINGS_BACKUP" "$APPSETTINGS_PATH"
fi

if [[ -f "$INSTALL_DIR/FleetManager.Agent" ]]; then
  chmod +x "$INSTALL_DIR/FleetManager.Agent"
fi

if [[ -f "$INSTALL_DIR/run-agent.sh" ]]; then
  chmod +x "$INSTALL_DIR/run-agent.sh"
fi

find "$COMMANDS_DIR" -maxdepth 1 -type f -name '*.sh' -exec chmod +x {} \;

nohup bash -lc "sleep $RESTART_DELAY_SECONDS; kill -TERM $AGENT_PARENT_PID >/dev/null 2>&1 || true" >/dev/null 2>&1 &

echo "Agent package updated from $BUNDLE_URL"
echo "Agent restart scheduled in $RESTART_DELAY_SECONDS seconds."
