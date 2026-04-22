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

verify_bundle_checksum() {
  local bundle_path="$1"
  local checksum="$2"
  local checksum_url="$3"

  if [[ -n "$checksum" ]]; then
    printf '%s  %s\n' "$checksum" "$bundle_path" | sha256sum -c -
    return 0
  fi

  if [[ -n "$checksum_url" ]]; then
    local checksum_path="$bundle_path.sha256"
    curl -fL "$checksum_url" -o "$checksum_path"
    (
      cd "$(dirname "$bundle_path")"
      sha256sum -c "$(basename "$checksum_path")"
    )
    return 0
  fi

  echo "No checksum provided for $(basename "$bundle_path"). Continuing without SHA256 verification."
}

stage_bundle() {
  local asset_name="$1"
  local bundle_url="$2"
  local checksum="$3"
  local checksum_url="$4"

  if [[ -z "$bundle_url" ]]; then
    echo "" && return 0
  fi

  local bundle_path="$WORK_DIR/$asset_name.zip"
  local extract_path="$WORK_DIR/$asset_name"

  curl -fL "$bundle_url" -o "$bundle_path"
  verify_bundle_checksum "$bundle_path" "$checksum" "$checksum_url"

  mkdir -p "$extract_path"
  unzip -oq "$bundle_path" -d "$extract_path"
  echo "$extract_path"
}

AGENT_INSTALL_DIR="/opt/fleetmanager-agent"
AGENT_COMMANDS_DIR="$AGENT_INSTALL_DIR/commands"
AGENT_APPSETTINGS_PATH="$AGENT_INSTALL_DIR/appsettings.json"
AGENT_RUNNER_TEMPLATE_RELATIVE_PATH="deploy/linux/run-agent.template.sh"

API_INSTALL_DIR="/opt/fleetmanager/api"
API_CONFIG_PATH="$API_INSTALL_DIR/appsettings.Production.json"

TMP_ROOT="${TMPDIR:-/tmp}/fleetmanager-stack-update"
WORK_DIR="$TMP_ROOT/$(date +%s)-$$"
AGENT_PARENT_PID="$PPID"

AGENT_BUNDLE_URL="$(json_get bundleUrl)"
AGENT_BUNDLE_SHA256_URL="$(json_get bundleSha256Url)"
AGENT_BUNDLE_SHA256="$(json_get bundleSha256)"

API_BUNDLE_URL="$(json_get apiBundleUrl)"
API_BUNDLE_SHA256_URL="$(json_get apiBundleSha256Url)"
API_BUNDLE_SHA256="$(json_get apiBundleSha256)"

RESTART_DELAY_SECONDS="$(json_get restartDelaySeconds)"
API_RESTART_DELAY_SECONDS="$(json_get apiRestartDelaySeconds)"

if [[ -z "$AGENT_BUNDLE_URL" ]]; then
  echo "bundleUrl is required." >&2
  exit 1
fi

if [[ -z "$RESTART_DELAY_SECONDS" ]]; then
  RESTART_DELAY_SECONDS="8"
fi

if [[ -z "$API_RESTART_DELAY_SECONDS" ]]; then
  API_RESTART_DELAY_SECONDS="$RESTART_DELAY_SECONDS"
fi

mkdir -p "$WORK_DIR"

cleanup() {
  rm -rf "$WORK_DIR" 2>/dev/null || true
}
trap cleanup EXIT

prepare_in_place_update_target() {
  local target_path="$1"
  local backup_path="${target_path}.previous"

  [[ -e "$backup_path" ]] && rm -rf "$backup_path"
  [[ -e "$target_path" ]] && mv "$target_path" "$backup_path"
}

AGENT_EXTRACT_DIR="$(stage_bundle "agent-bundle" "$AGENT_BUNDLE_URL" "$AGENT_BUNDLE_SHA256" "$AGENT_BUNDLE_SHA256_URL")"
if [[ ! -d "$AGENT_EXTRACT_DIR/agent" ]]; then
  echo "Agent bundle does not contain the agent directory." >&2
  exit 1
fi

if [[ ! -d "$AGENT_EXTRACT_DIR/deploy/linux/commands" ]]; then
  echo "Agent bundle does not contain updated command scripts." >&2
  exit 1
fi

AGENT_APPSETTINGS_BACKUP=""
if [[ -f "$AGENT_APPSETTINGS_PATH" ]]; then
  AGENT_APPSETTINGS_BACKUP="$WORK_DIR/agent-appsettings.json.backup"
  cp "$AGENT_APPSETTINGS_PATH" "$AGENT_APPSETTINGS_BACKUP"
fi

mkdir -p "$AGENT_INSTALL_DIR" "$AGENT_COMMANDS_DIR"
prepare_in_place_update_target "$AGENT_INSTALL_DIR/FleetManager.Agent"
prepare_in_place_update_target "$AGENT_INSTALL_DIR/FleetManager.Agent.dll"
cp -a "$AGENT_EXTRACT_DIR/agent/." "$AGENT_INSTALL_DIR/"
cp -a "$AGENT_EXTRACT_DIR/deploy/linux/commands/." "$AGENT_COMMANDS_DIR/"
rm -rf "$AGENT_INSTALL_DIR/FleetManager.Agent.previous" "$AGENT_INSTALL_DIR/FleetManager.Agent.dll.previous"

AGENT_RUNNER_TEMPLATE_PATH="$AGENT_EXTRACT_DIR/$AGENT_RUNNER_TEMPLATE_RELATIVE_PATH"
if [[ -f "$AGENT_RUNNER_TEMPLATE_PATH" ]]; then
  cp "$AGENT_RUNNER_TEMPLATE_PATH" "$AGENT_INSTALL_DIR/run-agent.sh"
fi

if [[ -n "$AGENT_APPSETTINGS_BACKUP" && -f "$AGENT_APPSETTINGS_BACKUP" ]]; then
  cp "$AGENT_APPSETTINGS_BACKUP" "$AGENT_APPSETTINGS_PATH"
fi

if [[ -f "$AGENT_INSTALL_DIR/FleetManager.Agent" ]]; then
  chmod +x "$AGENT_INSTALL_DIR/FleetManager.Agent"
fi

if [[ -f "$AGENT_INSTALL_DIR/run-agent.sh" ]]; then
  chmod +x "$AGENT_INSTALL_DIR/run-agent.sh"
fi

find "$AGENT_COMMANDS_DIR" -maxdepth 1 -type f -name '*.sh' -exec chmod +x {} \;

API_UPDATED=0
if [[ -n "$API_BUNDLE_URL" && -d "$API_INSTALL_DIR" ]]; then
  API_EXTRACT_DIR="$(stage_bundle "api-bundle" "$API_BUNDLE_URL" "$API_BUNDLE_SHA256" "$API_BUNDLE_SHA256_URL")"
  if [[ ! -f "$API_EXTRACT_DIR/FleetManager.Api" && ! -f "$API_EXTRACT_DIR/FleetManager.Api.dll" ]]; then
    echo "API bundle does not contain FleetManager.Api publish output." >&2
    exit 1
  fi

  API_CONFIG_BACKUP=""
  if [[ -f "$API_CONFIG_PATH" ]]; then
    API_CONFIG_BACKUP="$WORK_DIR/api-appsettings.Production.json.backup"
    cp "$API_CONFIG_PATH" "$API_CONFIG_BACKUP"
  fi

  prepare_in_place_update_target "$API_INSTALL_DIR/FleetManager.Api"
  prepare_in_place_update_target "$API_INSTALL_DIR/FleetManager.Api.dll"
  cp -a "$API_EXTRACT_DIR/." "$API_INSTALL_DIR/"
  rm -rf "$API_INSTALL_DIR/FleetManager.Api.previous" "$API_INSTALL_DIR/FleetManager.Api.dll.previous"

  if [[ -n "$API_CONFIG_BACKUP" && -f "$API_CONFIG_BACKUP" ]]; then
    cp "$API_CONFIG_BACKUP" "$API_CONFIG_PATH"
  fi

  if [[ -f "$API_INSTALL_DIR/FleetManager.Api" ]]; then
    chmod +x "$API_INSTALL_DIR/FleetManager.Api"
  fi

  API_UPDATED=1
fi

nohup bash -lc "sleep $RESTART_DELAY_SECONDS; kill -TERM $AGENT_PARENT_PID >/dev/null 2>&1 || true" >/dev/null 2>&1 &

if [[ "$API_UPDATED" -eq 1 ]]; then
  nohup bash -lc "sleep $API_RESTART_DELAY_SECONDS; pgrep -u fleetmgr -f '$API_INSTALL_DIR/FleetManager.Api' | xargs -r kill -TERM >/dev/null 2>&1 || true" >/dev/null 2>&1 &
fi

echo "Agent package updated from $AGENT_BUNDLE_URL"
if [[ "$API_UPDATED" -eq 1 ]]; then
  echo "API package updated from $API_BUNDLE_URL"
else
  echo "API package skipped on this node."
fi
echo "Agent restart scheduled in $RESTART_DELAY_SECONDS seconds."
if [[ "$API_UPDATED" -eq 1 ]]; then
  echo "API restart scheduled in $API_RESTART_DELAY_SECONDS seconds."
fi
