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
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

apt-get update
DEBIAN_FRONTEND=noninteractive apt-get install -y \
  ca-certificates curl python3 procps xz-utils \
  xvfb x11vnc fluxbox novnc websockify || true

if ! command -v chromium >/dev/null 2>&1 && ! command -v chromium-browser >/dev/null 2>&1; then
  DEBIAN_FRONTEND=noninteractive apt-get install -y chromium-browser || DEBIAN_FRONTEND=noninteractive apt-get install -y chromium || true
fi

id -u fleetmgr >/dev/null 2>&1 || useradd --system --create-home --home-dir /home/fleetmgr --shell /usr/sbin/nologin fleetmgr

mkdir -p "$INSTALL_DIR" "$DATA_DIR"
cp -R "$PACKAGE_DIR"/. "$INSTALL_DIR"/
mkdir -p "$INSTALL_DIR/commands"
cp -R "$SCRIPT_DIR/commands"/. "$INSTALL_DIR/commands"/

if [[ ! -f "$INSTALL_DIR/appsettings.json" ]]; then
  cp "$SCRIPT_DIR/appsettings.agent.template.json" "$INSTALL_DIR/appsettings.json"
fi

cp "$SCRIPT_DIR/fleetmanager-agent.service" "$SERVICE_PATH"
chown -R fleetmgr:fleetmgr "$INSTALL_DIR" "$DATA_DIR"
chmod +x "$INSTALL_DIR/FleetManager.Agent" "$INSTALL_DIR/commands/"*.sh

systemctl daemon-reload
systemctl enable fleetmanager-agent.service
systemctl restart fleetmanager-agent.service

echo "FleetManager agent installed on Ubuntu."
echo "Edit $INSTALL_DIR/appsettings.json with NodeId and BackendBaseUrl, or use register-node.sh."
