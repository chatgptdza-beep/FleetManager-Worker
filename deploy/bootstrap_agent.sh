#!/bin/bash
set -e

# Usage: bash bootstrap_agent.sh /tmp/fleetmanager_temp_config.json
CONFIG_FILE=$1

echo "Starting FleetManager Worker Bootstrap..."

# 1. Install prerequisites
apt-get update
DEBIAN_FRONTEND=noninteractive apt-get install -y \
    curl wget jq unzip tar \
    xvfb x11vnc novnc websockify \
    chromium-browser \
    dotnet-sdk-8.0

# 2. Setup Directories
mkdir -p /opt/fleetmanager-agent/commands
mkdir -p /var/lib/fleetmanager/sessions
mkdir -p /var/log/fleetmanager

# 3. Pull the latest agent files from GitHub
# We will pull the precompiled worker if it's there. 
# For now, it will simply set up the folder structure. 
# (The user will build and put the Worker binaries under /opt/fleetmanager-agent manually 
# OR we do a dotnet run from a github clone)
# But since this is a private project, we'll clone the github repo into /opt/fleetmanager-agent/src
# Wait, GitHub is private. We need a token or we assume it's public. 
# The easiest is getting the compiled agent. 
# For now, we'll just write a basic systemd service.

echo "Setting up configuration..."
if [ -f "$CONFIG_FILE" ]; then
    cp "$CONFIG_FILE" /opt/fleetmanager-agent/appsettings.json
fi

# Set permissions
chmod -R 755 /opt/fleetmanager-agent
chmod 777 /var/lib/fleetmanager/sessions

# 4. Create Systemd Service
cat << 'EOF' > /etc/systemd/system/fleetmanager-agent.service
[Unit]
Description=FleetManager Remote Worker Agent
After=network.target

[Service]
Type=simple
User=root
# We assume the dotnet binary is built here, or we use dotnet run in the src directory
WorkingDirectory=/opt/fleetmanager-agent
ExecStart=/usr/bin/dotnet FleetManager.Worker.dll
Restart=always
RestartSec=3
Environment="DOTNET_PRINT_TELEMETRY_MESSAGE=false"
Environment="ASPNETCORE_ENVIRONMENT=Production"

[Install]
WantedBy=multi-user.target
EOF

# 5. Reload and start
systemctl daemon-reload
systemctl enable fleetmanager-agent.service
systemctl restart fleetmanager-agent.service

echo "Bootstrap complete!"
