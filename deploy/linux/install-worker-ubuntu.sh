#!/usr/bin/env bash
set -euo pipefail

PACKAGE_DIR="${1:-}"
if [[ -z "$PACKAGE_DIR" || ! -d "$PACKAGE_DIR" ]]; then
  echo "Usage: install-worker-ubuntu.sh <published-agent-dir>" >&2
  exit 1
fi

INSTALL_DIR="/opt/fleetmanager-agent"
DATA_DIR="/var/lib/fleetmanager"
SERVICE_PATH="/etc/systemd/system/fleetmanager-agent.service"
RUNNER_PATH="$INSTALL_DIR/run-agent.sh"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

wait_for_apt_locks() {
  local waited=0
  while fuser /var/lib/apt/lists/lock /var/lib/dpkg/lock-frontend /var/cache/apt/archives/lock >/dev/null 2>&1; do
    echo "apt/dpkg is busy. Waiting for lock release..."
    sleep 5
    waited=$((waited + 5))
    if [[ "$waited" -ge 300 ]]; then
      echo "Timed out waiting for apt/dpkg lock release." >&2
      return 1
    fi
  done
}

retry_apt() {
  local retries=0
  while true; do
    wait_for_apt_locks || return 1
    if "$@"; then
      return 0
    fi

    if fuser /var/lib/apt/lists/lock /var/lib/dpkg/lock-frontend /var/cache/apt/archives/lock >/dev/null 2>&1; then
      retries=$((retries + 1))
      echo "apt/dpkg became busy again. Retrying..."
      sleep 5
      if [[ "$retries" -ge 60 ]]; then
        echo "Timed out retrying apt command while lock is held." >&2
        return 1
      fi
      continue
    fi

    return 1
  done
}

package_is_self_contained() {
  [[ -f "$PACKAGE_DIR/FleetManager.Agent" && -f "$PACKAGE_DIR/libhostfxr.so" ]]
}

ensure_dotnet_runtime() {
  local need_runtime=1
  if package_is_self_contained; then
    return 0
  fi

  if command -v dotnet >/dev/null 2>&1; then
    if dotnet --list-runtimes 2>/dev/null | grep -q '^Microsoft.NETCore.App 8\.'; then
      need_runtime=0
    fi
  fi

  if [[ "$need_runtime" -eq 0 ]]; then
    return 0
  fi

  retry_apt env DEBIAN_FRONTEND=noninteractive apt-get install -y dotnet-runtime-8.0 aspnetcore-runtime-8.0 && return 0

  # If Ubuntu repos do not provide dotnet packages, bootstrap Microsoft feed and retry.
  local ms_pkg="/tmp/packages-microsoft-prod.deb"
  curl -fsSL https://packages.microsoft.com/config/ubuntu/24.04/packages-microsoft-prod.deb -o "$ms_pkg"
  dpkg -i "$ms_pkg"
  rm -f "$ms_pkg"
  retry_apt apt-get update
  retry_apt env DEBIAN_FRONTEND=noninteractive apt-get install -y dotnet-runtime-8.0 aspnetcore-runtime-8.0
}

retry_apt apt-get update
retry_apt env DEBIAN_FRONTEND=noninteractive apt-get install -y \
  ca-certificates curl python3 procps xz-utils \
  xvfb x11vnc fluxbox novnc websockify xdg-utils \
  libatk1.0-0 libatk-bridge2.0-0 libatspi2.0-0 \
  libasound2t64 || true

if ! command -v chromium >/dev/null 2>&1 && ! command -v chromium-browser >/dev/null 2>&1; then
  retry_apt env DEBIAN_FRONTEND=noninteractive apt-get install -y chromium-browser || retry_apt env DEBIAN_FRONTEND=noninteractive apt-get install -y chromium || true
fi

ensure_dotnet_runtime

id -u fleetmgr >/dev/null 2>&1 || useradd --system --create-home --home-dir /home/fleetmgr --shell /usr/sbin/nologin fleetmgr

mkdir -p "$INSTALL_DIR" "$DATA_DIR"

# Worker writes command payload files here before script execution.
# Pre-create it with fleetmgr ownership to avoid permission errors.
mkdir -p /tmp/fleetmanager-agent
chown -R fleetmgr:fleetmgr /tmp/fleetmanager-agent
chmod 770 /tmp/fleetmanager-agent

if [[ -f "$PACKAGE_DIR/.fleetmanager.sha256" ]]; then
  (
    cd "$PACKAGE_DIR"
    sha256sum -c .fleetmanager.sha256
  )
fi

cp -R "$PACKAGE_DIR"/. "$INSTALL_DIR"/
mkdir -p "$INSTALL_DIR/commands"
cp -R "$SCRIPT_DIR/commands"/. "$INSTALL_DIR/commands"/

if [[ ! -f "$INSTALL_DIR/appsettings.json" ]]; then
  cp "$SCRIPT_DIR/appsettings.agent.template.json" "$INSTALL_DIR/appsettings.json"
fi

cp "$SCRIPT_DIR/fleetmanager-agent.service" "$SERVICE_PATH"
cat > "$RUNNER_PATH" <<'EOF'
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
EOF

chown -R fleetmgr:fleetmgr "$INSTALL_DIR" "$DATA_DIR"
if [[ -f "$INSTALL_DIR/FleetManager.Agent" ]]; then
  chmod +x "$INSTALL_DIR/FleetManager.Agent"
fi
chmod +x "$RUNNER_PATH" "$INSTALL_DIR/commands/"*.sh

systemctl daemon-reload
systemctl enable fleetmanager-agent.service
systemctl restart fleetmanager-agent.service

echo "FleetManager agent installed on Ubuntu."
echo "Edit $INSTALL_DIR/appsettings.json with NodeId and BackendBaseUrl, or use register-node.sh."
