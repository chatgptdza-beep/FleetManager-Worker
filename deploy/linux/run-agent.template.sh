#!/usr/bin/env bash
set -euo pipefail

INSTALL_DIR="/opt/fleetmanager-agent"
APPSETTINGS_PATH="${FM_APPSETTINGS_PATH:-$INSTALL_DIR/appsettings.json}"

resolve_viewer_host() {
  [[ -f "$APPSETTINGS_PATH" ]] || return 0

  python3 - "$APPSETTINGS_PATH" <<'PY'
import json, sys
try:
    with open(sys.argv[1], 'r', encoding='utf-8') as fh:
        data = json.load(fh)
    print(((data.get("Agent") or {}).get("NodeIpAddress") or "").strip())
except Exception:
    print("")
PY
}

VIEWER_HOST="$(resolve_viewer_host)"
if [[ -n "$VIEWER_HOST" ]]; then
  export FM_VIEWER_HOST="$VIEWER_HOST"
fi

if [[ -x "$INSTALL_DIR/FleetManager.Agent" ]]; then
  exec "$INSTALL_DIR/FleetManager.Agent"
fi

if [[ -f "$INSTALL_DIR/FleetManager.Agent.dll" ]]; then
  if ! command -v dotnet >/dev/null 2>&1; then
    echo "dotnet runtime is required to run FleetManager.Agent.dll." >&2
    exit 1
  fi

  exec dotnet "$INSTALL_DIR/FleetManager.Agent.dll"
fi

echo "FleetManager agent package is missing both FleetManager.Agent and FleetManager.Agent.dll." >&2
exit 1
