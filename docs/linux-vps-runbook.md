# Linux VPS Runbook

## What is implemented in this repo

- The desktop UI talks to the API:
  - `GET /api/nodes`
  - `GET /api/accounts?nodeId=...`
  - `POST /api/nodes/{nodeId}/commands`
- The VPS worker talks back to the API:
  - `POST /api/agent/heartbeat`
  - `GET /api/agent/nodes/{nodeId}/commands/next`
  - `POST /api/agent/commands/{commandId}/complete`

That means the control path is now:

1. Desktop sends a command to the API.
2. The API stores the command for one VPS.
3. The agent on that VPS polls for the next pending command.
4. The agent runs a local shell script for that command.
5. The agent reports success or failure back to the API.

## Important production note

The current API still uses the in-memory EF provider in `FleetManager.Infrastructure`.
That is fine for demo and local testing only.
For real VPS operations you must replace it with a persistent database before production use.

## Where each VPS is configured

There are two places:

1. API node record
   - Stored by `POST /api/nodes`
   - Fields: `Name`, `IpAddress`, `SshPort`, `ControlPort`, `SshUsername`, `OsType`, `Region`

2. Local agent config on the VPS
   - File: `/opt/fleetmanager-agent/appsettings.json`
   - Required fields:
     - `Agent:NodeId`
     - `Agent:BackendBaseUrl`
     - `Agent:HeartbeatIntervalSeconds`
     - `Agent:CommandPollIntervalSeconds`
     - `Agent:CommandScriptsPath`

The node record identifies the VPS in the control plane.
The local `appsettings.json` tells the worker which node it is and where the API is.

## Publish the Linux worker

From your build machine:

```powershell
dotnet publish "md file/01_full_project_latest/src/FleetManager.Agent/FleetManager.Agent.csproj" `
  -c Release `
  -r linux-x64 `
  --self-contained true `
  -o "md file/01_full_project_latest/out/agent/linux-x64"
```

Copy that published folder to the VPS.

## Register a VPS in the API

Use the helper script:

```bash
chmod +x deploy/linux/register-node.sh

./deploy/linux/register-node.sh \
  --api https://api.example.com \
  --name VPS-PAR-01 \
  --ip 10.0.0.21 \
  --ssh-user fleetmgr \
  --os Ubuntu \
  --region eu-west \
  --ssh-port 22 \
  --control-port 9001
```

If you already copied `appsettings.json` to the VPS, you can update it directly:

```bash
./deploy/linux/register-node.sh \
  --api https://api.example.com \
  --name VPS-PAR-01 \
  --ip 10.0.0.21 \
  --ssh-user fleetmgr \
  --os Ubuntu \
  --region eu-west \
  --appsettings /opt/fleetmanager-agent/appsettings.json
```

## Install on Ubuntu

```bash
chmod +x deploy/linux/install-worker-ubuntu.sh
sudo ./deploy/linux/install-worker-ubuntu.sh /tmp/fleetmanager-agent
```

## Install on CentOS / Rocky / Alma

```bash
chmod +x deploy/linux/install-worker-centos.sh
sudo ./deploy/linux/install-worker-centos.sh /tmp/fleetmanager-agent
```

## Prepare the VPS so communication stays stable

Use this checklist on every VPS:

1. Set a unique hostname.
2. Keep system time correct with `chronyd` or `systemd-timesyncd`.
3. Ensure outbound HTTPS from the VPS to the API is allowed.
4. Do not depend on inbound API -> VPS calls for normal control.
   - The worker uses outbound polling, which avoids many firewall and NAT issues.
5. Keep one dedicated service user for the agent:
   - `fleetmgr`
6. Keep one dedicated data root:
   - `/var/lib/fleetmanager`
7. Keep browser profile data per account/session, not shared.
8. Keep one display and one viewer slot per browser session.

## Why viewer conflicts happen

Viewer conflicts usually come from one of these:

1. Multiple browsers share one desktop display.
2. Multiple sessions share one VNC/noVNC port.
3. One viewer command opens a generic desktop instead of the account-specific browser.
4. Browser profiles are reused between accounts.

## How this repo avoids remote-viewer conflicts

The Linux command scripts use an account-specific session directory:

- `/var/lib/fleetmanager/sessions/<accountId>/`

For each account they allocate:

- one X display
- one VNC port
- one noVNC web port
- one browser remote debugging port

That means `OpenAssignedSession` always points to one account session instead of a shared desktop.

## Recommended viewer rule

Use one viewer stack per account:

- display: `:100 + slot`
- VNC port: `5900 + slot`
- noVNC port: `6900 + slot`
- browser debug port: `9222 + slot`

Do not reuse those ports for a second account.

## Commands executed by the Linux worker

Each command maps to one shell script in:

- `/opt/fleetmanager-agent/commands`

Implemented scripts in this repo:

- `StartBrowser.sh`
- `StopBrowser.sh`
- `LoginWorkflow.sh`
- `StartAutomation.sh`
- `StopAutomation.sh`
- `PauseAutomation.sh`
- `OpenAssignedSession.sh`
- `BringManagedWindowToFront.sh`
- `FetchSessionLogs.sh`

## Suggested firewall model

Recommended:

- Desktop -> API: allowed
- VPS worker -> API: allowed
- Desktop -> noVNC viewer: through VPN, reverse proxy, or bastion

Avoid exposing raw viewer ports directly to the public internet if possible.

## How to inspect a browser from the UI

When you click `View Browser`, the desktop sends `OpenAssignedSession`.
The worker:

1. allocates the account viewer slot
2. ensures Xvfb / x11vnc / noVNC are running for that account
3. starts the browser if needed
4. returns the viewer URL in the command result

## What is still your next production task

Before real deployment, you should still add:

1. persistent database instead of in-memory EF
2. authentication between agent and API
3. HTTPS certificates trusted by the VPS
4. a secure viewer gateway instead of open viewer ports
5. real browser automation implementation behind the command scripts
