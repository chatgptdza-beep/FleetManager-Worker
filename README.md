# FleetManager - VPS Agent and Browser Automation Fleet

FleetManager is a self-hosted control plane for browser automation workers running on Linux VPS nodes. The current solution contains:

- `FleetManager.Api` for REST, SignalR, auth, and orchestration.
- `FleetManager.Agent` for heartbeat, command polling, and node-side execution.
- `FleetManager.Desktop` for the operator dashboard and node provisioning.
- `FleetManager.Infrastructure` for EF Core persistence and repositories.
- `FleetManager.Api.Tests` for unit and integration coverage.

## Current Direction

The codebase now follows these rules:

- The API is the source of truth for accounts, commands, node state, and proxy rotation.
- Desktop demo data is explicit demo mode, not a silent fallback for failed writes.
- Agent control routes accept operator JWT or machine API key.
- Node command dequeue is atomic on relational databases.
- Desktop updates node state incrementally from SignalR instead of always forcing a full reload.
- Closing the Desktop app does not stop the VPS worker. The agent keeps polling, heartbeating, and reacting to server-side commands independently.
- Manual takeover events are persisted in the API as alerts and workflow stages, so the Desktop can recover pending VNC links after it is opened again.
- Worker-side events that happen while the Desktop is closed are stored in a server-side worker inbox and shown after the operator reconnects.
- Account rows now support right-click proxy injection from the Desktop, with proxy pool updates synchronized back through the API.
- Proxy injection accepts a `.txt` list loaded from the Desktop UI using one proxy per line in either `ip:port` or `ip:port:user:password` format.

## Local Development

### Required configuration

Set these before running the API outside development defaults:

```powershell
$env:ConnectionStrings__DefaultConnection = "Host=localhost;Database=FleetManagerDb;Username=postgres;Password=postgres"
$env:Jwt__Key = "<32+ char secret>"
$env:AdminPassword = "<operator password>"
$env:AgentApiKey = "<agent api key>"
```

Optional:

```powershell
$env:Cors__AllowedOrigins__0 = "https://ops.example.com"
$env:SeedDemoData = "false"
```

Desktop-specific overrides:

```powershell
$env:FLEETMANAGER_API_BASE_URL = "http://localhost:5188/"
$env:FLEETMANAGER_API_PASSWORD = "<operator password>"
$env:FLEETMANAGER_AGENT_API_KEY = "<agent api key>"
$env:FLEETMANAGER_DESKTOP_DEMO_MODE = "false"
```

### Build and test

```powershell
dotnet build FleetManager.sln
dotnet test FleetManager.sln
```

## Agent Packaging

Publish a Linux package locally before provisioning a node from the Desktop app:

```powershell
.\scripts\publish-agent.ps1
```

This writes the unpacked package to `out/agent` and also creates a single GitHub-ready bundle at `out/bundles/fleetmanager-agent-bundle-linux-x64.zip`.
The publish step also writes `.fleetmanager.sha256` for the unpacked agent folder and a bundle SHA256 file beside the zip.
The Desktop SSH installer now downloads that prebuilt bundle from GitHub instead of cloning and building the repository on the VPS.

Recommended GitHub path for the bundle:

```text
deploy/artifacts/fleetmanager-agent-bundle-linux-x64.zip
```

Optional overrides:

```powershell
$env:FLEETMANAGER_AGENT_BUNDLE_URL = "https://raw.githubusercontent.com/<owner>/<repo>/main/deploy/artifacts/fleetmanager-agent-bundle-linux-x64.zip"
$env:FLEETMANAGER_AGENT_BUNDLE_SHA256 = "<sha256 of the zip file>"
```

Manual publish example:

```powershell
dotnet publish .\src\FleetManager.Agent\FleetManager.Agent.csproj `
  -c Release `
  -r linux-x64 `
  --self-contained `
  -o .\out\agent
```

## Ubuntu Node Install

### 1. Publish agent

```powershell
.\scripts\publish-agent.ps1
```

### 2. Download the bundle on the VPS

```bash
curl -fsSL https://raw.githubusercontent.com/<owner>/<repo>/main/deploy/artifacts/fleetmanager-agent-bundle-linux-x64.zip -o /tmp/fleetmanager-agent-bundle.zip
mkdir -p /tmp/fleetmanager-bundle
unzip -oq /tmp/fleetmanager-agent-bundle.zip -d /tmp/fleetmanager-bundle
```

### 3. Install the service

```bash
ssh user@VPS_IP "sudo bash /tmp/fleetmanager-bundle/deploy/linux/install-worker-ubuntu.sh /tmp/fleetmanager-bundle/agent"
```

### 4. Register the node

```bash
bash /tmp/fleetmanager-bundle/deploy/linux/register-node.sh \
  --api https://your-api.example.com \
  --admin-password '<operator password>' \
  --name VPS-01 \
  --ip 10.0.0.21 \
  --ssh-user fleetmgr \
  --os Ubuntu \
  --region eu-west
```

### 5. Configure appsettings on the VPS if needed

The worker reads `/opt/fleetmanager-agent/appsettings.json`.

Example:

```json
{
  "Agent": {
    "NodeId": "00000000-0000-0000-0000-000000000000",
    "BackendBaseUrl": "https://your-api.example.com",
    "ApiKey": "<agent api key>",
    "HeartbeatIntervalSeconds": 15,
    "CommandPollIntervalSeconds": 3,
    "AgentVersion": "1.0.0",
    "ControlPort": 9001,
    "ConnectionState": "Connected",
    "ConnectionTimeoutSeconds": 5,
    "CommandScriptsPath": "/opt/fleetmanager-agent/commands",
    "NodeIpAddress": "10.0.0.21"
  }
}
```

## QuickReserve Extension Rollout (New VPS)

The agent now supports VPS-wide unpacked browser extensions through `Agent.BrowserExtensions` and `FM_BROWSER_EXTENSIONS`.
`StartBrowser.sh` stages each extension into the account profile and launches Chromium with:

- `--disable-extensions-except=<staged_paths>`
- `--load-extension=<staged_paths>`

Use the automation script below when adding a new VPS so extension setup is reproducible and GitHub-backed:

```powershell
.\scripts\setup-vps-extension-and-launcher-bridge.ps1 `
  -VpsIp 89.116.26.182 `
  -RootPassword '<root-password>' `
  -LocalExtensionPath 'C:\Users\<you>\Desktop\QuickReserve\QuickReserve Loader' `
  -StartLauncherTunnel
```

What this script does:

- Uploads the unpacked extension to `/opt/fleetmanager-agent/extensions/quickreserve-loader`.
- Normalizes nested folder layouts and verifies `manifest.json` on VPS.
- Writes `Agent.BrowserExtensions` in `/opt/fleetmanager-agent/appsettings.json`.
- Writes systemd override `FM_BROWSER_EXTENSIONS=...` for immediate runtime parity.
- Restarts `fleetmanager-agent` and verifies service health.
- Optionally starts local reverse tunnel to VPS for Launcher localhost ports.

Default Launcher bridge ports are:

- `45321`
- `65430`
- `65475`

These can be overridden with `-LauncherPorts`.

## Capacity Notes (50 Browsers)

Ports are not the bottleneck for 50 browser sessions. The main constraints are CPU, RAM, and browser rendering overhead on each VPS.

- A single Launcher port can multiplex many connections; using `45321/65430/65475` is fine for dozens of sessions.
- For 50 concurrent Chromium sessions, plan by node resources first.
- Practical baseline: start around 10-20 sessions per medium VPS, then scale horizontally to more VPS nodes.
- Monitor and enforce limits using node metrics: CPU under sustained 80-85%, RAM headroom at least 20%.
- Keep reverse tunnel stable with keepalive options (`ServerAliveInterval`, `ServerAliveCountMax`) when using SSH.

## Service Management

```bash
sudo systemctl status fleetmanager-agent
sudo journalctl -u fleetmanager-agent -f
sudo systemctl restart fleetmanager-agent
```

The Linux service now launches through `/opt/fleetmanager-agent/run-agent.sh`, which supports either:

- a self-contained binary package named `FleetManager.Agent`
- a framework-dependent package containing `FleetManager.Agent.dll`

## Desktop Restart Behavior

- If the Desktop app is closed, the `fleetmanager-agent` service on each VPS keeps running normally.
- Existing commands continue to execute because they are stored in the API, not in the Desktop process.
- Worker-side manual takeover requests are stored as active alerts with the VNC URL, so reopening the Desktop restores those pending interactions instead of losing them.
- Proxy rotations and worker command failures are also queued in the API worker inbox until the operator acknowledges them from the Desktop.
- Completing a manual takeover now clears the pending alert and returns the account to `Stable`, ready for the next operator command.

## Security Notes

- Do not rely on development fallback secrets in non-development environments.
- Store `AgentApiKey`, `AdminPassword`, and `Jwt__Key` in environment variables or a secret store.
- SSH secrets are no longer persisted as node records in the API.
- CORS should be restricted through `Cors__AllowedOrigins`.

## Remaining Follow-up

- Replace remaining dashboard-wide reloads with narrower account-level projections.
- Add end-to-end coverage for provisioning and SignalR reconnect scenarios.
- Introduce signed or checksum-verified release artifacts for agent packages.
