#!/bin/bash
# =============================================================
# FleetManager API - Deploy & Purge demo data
# Run on the server: bash deploy_api.sh
# =============================================================
set -euo pipefail

API_DIR="/opt/fleetmanager/api"
SERVICE_NAME="fleetmanager-api"

echo "🛑 [1] Stopping API service..."
sudo systemctl stop "$SERVICE_NAME" 2>/dev/null || true

echo "📦 [2] Backing up existing deployment..."
if [ -d "$API_DIR" ]; then
    sudo cp -r "$API_DIR" "${API_DIR}.backup.$(date +%Y%m%d%H%M%S)"
fi

echo "📂 [3] Deploying new API files..."
sudo mkdir -p "$API_DIR"
sudo cp -r /tmp/fleet-api-deploy/* "$API_DIR/"
sudo chmod +x "$API_DIR/FleetManager.Api"

echo "🚀 [4] Starting API service (auto-purge demo data on startup)..."
sudo systemctl start "$SERVICE_NAME"

echo "⏳ [5] Waiting for API to become ready..."
sleep 3

echo "✅ Deployment complete! Demo data will be purged on first request."
echo "   API URL: http://$(hostname -I | awk '{print $1}'):5000/"
