#!/bin/bash
# =======================================================
# FleetManager - 자동 VPS التجهيز الكامل لـ
# =======================================================

NODE_ID=$1
API_URL=$2
API_KEY=$3

echo "🚀 [1] تحديث السيرفر وتثبيت المتطلبات الأساسية..."
sudo apt update && sudo apt upgrade -y
sudo apt install -y curl wget git unzip xvfb x11vnc novnc fluxbox python3-websockify

echo "🐳 [2] تثبيت Docker Engine..."
if ! command -v docker &> /dev/null; then
    curl -fsSL https://get.docker.com -o get-docker.sh
    sudo sh get-docker.sh
    sudo usermod -aG docker root
    rm get-docker.sh
fi

echo "📁 [3] إنشاء المجلدات الأساسية لـ FleetManager..."
sudo mkdir -p /opt/fleetmanager/{agent,profiles,scripts,logs}
sudo chown -R root:root /opt/fleetmanager

echo "⚙️ [4] إنشاء Systemd Service ليعمل الـ Agent دائماً..."
cat <<EOF | sudo tee /etc/systemd/system/fleetmanager-agent.service
[Unit]
Description=FleetManager VPS Agent
After=network.target docker.service

[Service]
ExecStart=/opt/fleetmanager/agent/FleetManager.Agent
WorkingDirectory=/opt/fleetmanager/agent
Restart=always
RestartSec=5
Environment="NodeId=${NODE_ID}"
Environment="ApiBaseUrl=${API_URL}"
Environment="ApiKey=${API_KEY}"

[Install]
WantedBy=multi-user.target
EOF

sudo systemctl daemon-reload
sudo systemctl enable fleetmanager-agent.service

echo "✅ اكتمل التثبيت بنجاح للـ Node: $NODE_ID"
