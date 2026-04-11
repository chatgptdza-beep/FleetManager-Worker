#!/bin/bash
NODE_ID=$1
API_URL=$2

echo "Installing Fleet Manager Worker for Node: $NODE_ID"
echo "Targeting API: $API_URL"

# 1. Update system
sudo apt-get update && sudo apt-get install -y dotnet-runtime-8.0 wget unzip

# 2. Create directory
sudo mkdir -p /opt/fleetmanager-agent
cd /opt/fleetmanager-agent

# 3. Download your latest binary from GitHub Releases or directly
# Replace the URL below with your actual binary download link
# wget https://github.com/YOUR_USER/YOUR_REPO/releases/download/v1.0.0/agent-linux-x64.zip
# unzip agent-linux-x64.zip

# 4. Generate appsettings.json
cat <<EOF | sudo tee /opt/fleetmanager-agent/appsettings.json
{
  "NodeId": "$NODE_ID",
  "BackendBaseUrl": "$API_URL",
  "AgentVersion": "1.0.0"
}
EOF

# 5. Start Service (Simplified)
# sudo ./FleetManager.Agent
