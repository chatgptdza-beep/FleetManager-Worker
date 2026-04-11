# Secure VPS Management & Agent Onboarding Specification

## Purpose
This document defines a **safe administrative design** for a VPS management system that can:
- register VPS nodes from a central dashboard,
- connect over SSH,
- install and update an agent automatically,
- manage browser/session workloads,
- collect health metrics and logs,
- execute a limited set of remote actions,
- preserve traceability and operational control.

It is written as a secure replacement for any approach that depends on:
- persistent `root` password entry,
- unrestricted "full access" agents,
- opening all firewall ports,
- arbitrary file modification,
- unrestricted remote shell execution from the dashboard.

---

## Non-Goals
The system must **not** be designed around:
- storing reusable root passwords in the application,
- enabling blanket remote control over the host,
- disabling firewall protections,
- allowing arbitrary command execution from operators,
- granting the agent unrestricted filesystem access,
- silently changing system files outside approved paths.

---

## High-Level Architecture

### Components
1. **Operator Dashboard**
   - Adds VPS nodes
   - Monitors health and sessions
   - Sends approved remote commands
   - Reviews logs and alerts

2. **Central Backend API**
   - Stores node metadata
   - Orchestrates installation jobs
   - Issues short-lived enrollment tokens
   - Maintains audit trails
   - Publishes real-time status updates

3. **Bootstrap Installer**
   - Runs once during node onboarding
   - Validates prerequisites
   - Installs the agent
   - Registers the node with the backend

4. **Node Agent**
   - Sends heartbeat and metrics
   - Manages controlled browser/session lifecycle
   - Executes only allowlisted commands
   - Streams logs back to the backend

5. **Secrets Store**
   - Stores private keys, enrollment tokens, and encrypted credentials
   - Prevents plaintext secret storage in the application database

---

## Secure Onboarding Model

### Recommended Access Model
Use one of the following:

#### Preferred: SSH Key + Limited Admin User
- Create a dedicated user such as `deploy` or `nodeadmin`
- Disable direct password-based root login where possible
- Use SSH keys for authentication
- Permit `sudo` only for the exact installation and service-management operations required

#### Acceptable for First Bootstrap: One-Time Secret
- Use a one-time password or one-time bootstrap token
- Rotate or invalidate it immediately after successful install
- Do not reuse it for routine management

### Never Use as the Default Model
- Permanent root passwords stored in the dashboard
- Shared credentials across nodes
- Reusable plaintext secrets in database rows or config files

---

## Add VPS Form Specification

### Required Fields
- `Node Name`
- `IP Address`
- `SSH Port`
- `Username`
- `Authentication Type`
  - SSH Key
  - One-Time Password
  - Bootstrap Token
- `OS Type`
- `Expected Agent Group`
- `Region / Label`

### Optional Fields
- `Tags`
- `Max Browser Sessions`
- `Notes`
- `Install Profile`
- `Firewall Profile`

### Validation Rules
- IP must be valid IPv4 or IPv6
- SSH port must be numeric and within allowed range
- Username must not be empty
- Install profile must exist
- Firewall profile must exist
- Node name must be unique

---

## Onboarding Workflow

### Step 1: Register Node in Dashboard
Operator enters:
- node name,
- IP,
- SSH port,
- admin username,
- auth method,
- install profile.

Backend creates an `InstallJob` with status `Pending`.

### Step 2: Preflight Validation
The installer performs checks for:
- SSH reachability
- host key verification
- supported OS/version
- disk space
- memory threshold
- required packages/tools
- write access to agent install directory
- service manager availability (`systemd` or equivalent)

If any check fails, onboarding stops and logs the exact failed step.

### Step 3: Bootstrap Install
The bootstrap process may do only the following approved actions:
- create agent directory,
- place binaries and config files,
- create a service unit,
- create or validate a dedicated runtime user,
- set file ownership for the agent directory,
- open only required firewall rules,
- start and enable the agent service.

### Step 4: Agent Enrollment
Agent obtains a short-lived registration token and sends:
- node fingerprint,
- agent version,
- OS info,
- capabilities,
- initial health snapshot.

Backend marks the node `Online` only after successful enrollment.

### Step 5: Post-Install Hardening
After enrollment:
- invalidate bootstrap secret,
- rotate temporary credentials,
- record approved capabilities,
- verify outbound connectivity only to required services,
- enable monitoring.

---

## Allowed Agent Capabilities
The agent should support **explicit, limited capabilities** instead of unlimited control.

### Health & Telemetry
- CPU/RAM/Disk collection
- heartbeat
- service status
- browser/session counts
- selected application metrics

### Browser & Session Operations
- start browser
- stop browser
- restart browser worker
- open assigned session
- close assigned session
- bring managed window to front
- capture screenshot
- fetch controlled session logs

### Service Operations
- restart agent service
- update agent package
- reload approved config

### Explicitly Forbidden
- arbitrary shell execution from dashboard
- editing arbitrary files anywhere on host
- reading arbitrary secrets from filesystem
- changing firewall rules outside approved profiles
- modifying user accounts outside installation scope
- downloading and executing unapproved binaries

---

## Filesystem Layout
A safe filesystem layout could be:

```text
/opt/fleet-agent/
  bin/
  config/
  logs/
  runtime/
  browser-profiles/
  updates/
```

### Ownership
- application files owned by dedicated service user
- writable directories restricted to agent runtime paths only
- no broad write permission outside approved directories

---

## Firewall Policy

### Principle
Use **default deny** and allow only the ports and directions required.

### Typical Allowed Rules
- inbound SSH only from admin IPs or VPN ranges
- outbound HTTPS to backend/API endpoints
- outbound DNS if required
- internal-only browser debug ports bound to localhost or tunnel interface

### Forbidden Design
- opening all inbound ports
- exposing browser debug ports publicly
- wildcard firewall exceptions without scope

### Firewall Profiles
Example profiles:
- `minimal-node`
- `browser-managed`
- `maintenance-window`

Each profile should be versioned and auditable.

---

## Secrets Handling

### Rules
- never store secrets in plaintext in operational tables
- encrypt secrets at rest
- prefer references to a secrets manager over direct values
- support rotation
- redact secrets from logs and UI

### Recommended Secret Types
- SSH private key reference
- one-time bootstrap secret
- agent enrollment token
- proxy credentials reference
- browser session secret reference

---

## Audit & Accountability
Every sensitive action must generate an audit entry.

### Audit Fields
- actor
- target node
- target session
- action type
- result
- timestamp
- source IP
- correlation ID

### Actions to Audit
- add node
- start install
- retry install
- update config
- restart service
- execute browser action
- acknowledge alert
- update firewall profile

---

## Data Model

### `vps_nodes`
- `id`
- `name`
- `ip_address`
- `ssh_port`
- `ssh_username`
- `auth_type`
- `os_type`
- `status`
- `install_profile_id`
- `firewall_profile_id`
- `last_heartbeat_at`
- `created_at`
- `updated_at`

### `vps_credentials`
- `id`
- `vps_node_id`
- `credential_type`
- `secret_reference`
- `is_temporary`
- `created_at`
- `expires_at`

### `agent_install_jobs`
- `id`
- `vps_node_id`
- `job_status`
- `current_step`
- `started_at`
- `ended_at`
- `error_message`
- `log_reference`

### `node_capabilities`
- `id`
- `vps_node_id`
- `can_start_browser`
- `can_stop_browser`
- `can_restart_browser`
- `can_capture_screenshot`
- `can_fetch_logs`
- `can_update_agent`

### `firewall_profiles`
- `id`
- `name`
- `description`
- `rules_json`
- `version`
- `created_at`

### `node_commands`
- `id`
- `vps_node_id`
- `command_type`
- `payload_json`
- `status`
- `requested_by_user_id`
- `created_at`
- `executed_at`
- `result_message`

### `audit_logs`
- `id`
- `actor_user_id`
- `target_type`
- `target_id`
- `action_type`
- `details_json`
- `result_status`
- `created_at`

---

## Status Model

### Node Status
- `Pending`
- `Installing`
- `Online`
- `Degraded`
- `Offline`
- `InstallFailed`
- `Maintenance`

### Install Job Status
- `Pending`
- `Running`
- `Succeeded`
- `Failed`
- `Cancelled`

### Command Status
- `Pending`
- `Dispatched`
- `Executed`
- `Failed`
- `TimedOut`

---

## Installation Profiles

### Example: `linux-browser-node-v1`
Includes:
- agent package
- managed browser runtime
- service definition
- log rotation config
- minimal firewall profile
- allowed capability set

### Profile Rules
- versioned
- immutable once published
- changes must create a new version
- rollout can be staged by group

---

## Browser Management Model

### Managed Browser Features
- isolated profile directories
- per-session launch options
- local-only debug endpoints unless tunneled securely
- controlled start/stop lifecycle
- screenshot capture
- recent console/network summary collection

### Not Allowed
- unrestricted browsing control over unrelated applications
- attaching to arbitrary processes on the host
- exposing remote debugging publicly

---

## Error Handling

### Install Failures Should Surface
- connection failed
- auth failed
- unsupported OS
- insufficient disk
- firewall apply failed
- service start failed
- enrollment token invalid
- capability registration failed

### Recovery Controls
- retry last failed step
- rerun install job
- re-enroll agent
- roll back to previous agent version

---

## Minimal API Contract

### Backend
- `POST /api/nodes`
- `POST /api/nodes/{id}/install`
- `GET /api/nodes/{id}`
- `GET /api/nodes/{id}/install-jobs`
- `POST /api/nodes/{id}/commands`
- `GET /api/nodes/{id}/logs`
- `POST /api/nodes/{id}/firewall-profile`

### Agent
- `POST /api/agent/enroll`
- `POST /api/agent/heartbeat`
- `POST /api/agent/command-result`
- `POST /api/agent/log-batch`

---

## Implementation Notes

### Desktop / UI
Recommended stack:
- WPF or WinForms for Windows operator UI
- real-time updates via SignalR

### Backend
Recommended stack:
- ASP.NET Core
- PostgreSQL
- Redis for queues/cache
- Serilog for structured logging

### Agent
Recommended stack:
- .NET worker service
- signed agent packages
- versioned update channel

---

## Safe Operational Defaults
- root login disabled where possible
- SSH password login disabled after onboarding when feasible
- allowlist-only commands
- default deny firewall
- secrets redacted in UI/logs
- every command auditable
- temporary bootstrap credentials rotated immediately
- agent runs with least privilege required

---

## What This Gives You
This design still supports centralized administration, automated node setup, browser/session orchestration, and operational control, but without relying on dangerous defaults such as persistent root passwords, unrestricted host access, or fully open firewalls.

---

## Next Deliverables
The next documents to produce from this specification are:
1. Add VPS screen wireframe
2. Installation job state machine
3. agent config schema
4. firewall profile schema
5. backend API DTOs
6. database ERD
7. operator permissions matrix
