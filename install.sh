#!/bin/bash
set -euo pipefail

NODE_ID="${1:-}"
API_URL="${2:-}"

if [[ -z "$NODE_ID" || -z "$API_URL" ]]; then
  echo "Usage: $0 <NODE_ID> <API_URL>"
  echo "  NODE_ID  - Unique identifier for this node"
  echo "  API_URL  - Base URL of the Fleet Manager backend (e.g. https://fleet.example.com)"
  exit 1
fi

INSTALL_DIR="/opt/fleetmanager-agent"
SERVICE_NAME="fleetmanager-agent"
SERVICE_USER="fleetmanager"
AGENT_VERSION="1.0.0"
GITHUB_REPO="chatgptdza-beep/FleetManager-Worker"
RELEASE_ASSET="agent-linux-x64.zip"

echo "Installing Fleet Manager Worker for Node: $NODE_ID"
echo "Targeting API: $API_URL"

# 1. Update system and install dependencies
echo ">> Updating system and installing dependencies..."
sudo apt-get update -qq
sudo apt-get install -y dotnet-runtime-8.0 wget unzip

# 2. Create dedicated service user
if ! id -u "$SERVICE_USER" &>/dev/null; then
  echo ">> Creating service user: $SERVICE_USER"
  sudo useradd --system --no-create-home --shell /usr/sbin/nologin "$SERVICE_USER"
fi

# 3. Create install directory
echo ">> Creating install directory: $INSTALL_DIR"
sudo mkdir -p "$INSTALL_DIR"

# 4. Download the latest release binary from GitHub
echo ">> Downloading agent binary from GitHub Releases..."
DOWNLOAD_URL="https://github.com/${GITHUB_REPO}/releases/latest/download/${RELEASE_ASSET}"
sudo wget -q "$DOWNLOAD_URL" -O "/tmp/${RELEASE_ASSET}"
sudo unzip -o "/tmp/${RELEASE_ASSET}" -d "$INSTALL_DIR"
sudo chmod +x "${INSTALL_DIR}/FleetManager.Agent"
sudo rm "/tmp/${RELEASE_ASSET}"

# 5. Write appsettings.json
echo ">> Writing configuration..."
sudo tee "${INSTALL_DIR}/appsettings.json" > /dev/null <<EOF
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.Hosting.Lifetime": "Information"
    }
  },
  "Agent": {
    "NodeId": "$NODE_ID",
    "BackendBaseUrl": "$API_URL",
    "AgentVersion": "$AGENT_VERSION",
    "HeartbeatIntervalSeconds": 30
  }
}
EOF

# Lock down permissions so only the service user can read the config
sudo chown -R "$SERVICE_USER":"$SERVICE_USER" "$INSTALL_DIR"
sudo chmod 750 "$INSTALL_DIR"
sudo chmod 640 "${INSTALL_DIR}/appsettings.json"

# 6. Install and start systemd service
echo ">> Installing systemd service: $SERVICE_NAME"
sudo tee "/etc/systemd/system/${SERVICE_NAME}.service" > /dev/null <<EOF
[Unit]
Description=Fleet Manager Worker Agent
After=network.target

[Service]
WorkingDirectory=$INSTALL_DIR
ExecStart=${INSTALL_DIR}/FleetManager.Agent
Restart=always
RestartSec=10
SyslogIdentifier=$SERVICE_NAME
User=$SERVICE_USER
Environment=DOTNET_ENVIRONMENT=Production

[Install]
WantedBy=multi-user.target
EOF

sudo systemctl daemon-reload
sudo systemctl enable "$SERVICE_NAME"
sudo systemctl restart "$SERVICE_NAME"

echo ">> Fleet Manager Worker installed and started successfully."
echo "   Check status: sudo systemctl status $SERVICE_NAME"
echo "   View logs:    sudo journalctl -u $SERVICE_NAME -f"
