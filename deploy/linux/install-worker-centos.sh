#!/usr/bin/env bash
set -euo pipefail

PACKAGE_DIR="${1:-}"
if [[ -z "$PACKAGE_DIR" || ! -d "$PACKAGE_DIR" ]]; then
  echo "Usage: install-worker-centos.sh <published-agent-dir>" >&2
  exit 1
fi

INSTALL_DIR="/opt/fleetmanager-agent"
DATA_DIR="/var/lib/fleetmanager"
SERVICE_PATH="/etc/systemd/system/fleetmanager-agent.service"
RUNNER_PATH="$INSTALL_DIR/run-agent.sh"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

dnf install -y epel-release || true
dnf install -y \
  ca-certificates curl python3 procps-ng tar \
  xorg-x11-server-Xvfb x11vnc fluxbox novnc python3-websockify || true
dnf install -y chromium || true

id -u fleetmgr >/dev/null 2>&1 || useradd --system --create-home --home-dir /home/fleetmgr --shell /sbin/nologin fleetmgr

mkdir -p "$INSTALL_DIR" "$DATA_DIR"

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

echo "FleetManager agent installed on CentOS/Rocky/Alma."
echo "Edit $INSTALL_DIR/appsettings.json with NodeId and BackendBaseUrl, or use register-node.sh."
