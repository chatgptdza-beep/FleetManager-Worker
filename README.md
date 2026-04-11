# FleetManager Worker Agent

A lightweight .NET 8 background service that runs on managed nodes and periodically sends heartbeats to the Fleet Manager backend API.

## Features

- Reads node identity and backend URL from `appsettings.json`
- Sends periodic heartbeat `POST` requests to `{BackendBaseUrl}/api/nodes/{nodeId}/heartbeat`
- Configurable heartbeat interval (default: 30 seconds)
- Structured logging via `Microsoft.Extensions.Logging`

## Quick Install (Linux)

```bash
curl -sSL https://raw.githubusercontent.com/chatgptdza-beep/FleetManager-Worker/main/install.sh | \
  sudo bash -s -- <NODE_ID> <API_URL>
```

**Example:**
```bash
curl -sSL https://raw.githubusercontent.com/chatgptdza-beep/FleetManager-Worker/main/install.sh | \
  sudo bash -s -- node-001 https://fleet.example.com
```

The script will:
1. Install `dotnet-runtime-8.0` and required tools
2. Download the latest `agent-linux-x64.zip` from GitHub Releases
3. Write `/opt/fleetmanager-agent/appsettings.json` with your node settings
4. Register and start the `fleetmanager-agent` systemd service

## Configuration

The agent is configured via `/opt/fleetmanager-agent/appsettings.json`:

```json
{
  "Agent": {
    "NodeId": "node-001",
    "BackendBaseUrl": "https://fleet.example.com",
    "AgentVersion": "1.0.0",
    "HeartbeatIntervalSeconds": 30
  }
}
```

| Key                      | Description                                    | Default |
|--------------------------|------------------------------------------------|---------|
| `NodeId`                 | Unique identifier for this node                | _(required)_ |
| `BackendBaseUrl`         | Base URL of the Fleet Manager backend          | _(required)_ |
| `AgentVersion`           | Version string reported in heartbeats          | `1.0.0` |
| `HeartbeatIntervalSeconds` | Seconds between heartbeat requests           | `30`    |

## Service Management

```bash
# Check status
sudo systemctl status fleetmanager-agent

# View live logs
sudo journalctl -u fleetmanager-agent -f

# Restart
sudo systemctl restart fleetmanager-agent
```

## Building from Source

### Prerequisites
- .NET 8 SDK

```bash
dotnet restore FleetManager.Agent/FleetManager.Agent.csproj
dotnet build   FleetManager.Agent/FleetManager.Agent.csproj -c Release
```

### Publish self-contained package

```bash
dotnet publish FleetManager.Agent/FleetManager.Agent.csproj \
  -c Release -r linux-x64 --self-contained false \
  -o publish/linux-x64
```

## Releases

Pushing a tag in the format `vX.Y.Z` triggers the [GitHub Actions workflow](.github/workflows/release.yml), which builds and attaches `agent-linux-x64.zip` to the release.

