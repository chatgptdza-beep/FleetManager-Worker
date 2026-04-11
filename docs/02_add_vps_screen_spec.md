# Add VPS Screen Specification

## Goal
Allow an operator to register a VPS node, validate connectivity, select an installation profile, and launch a secure onboarding job.

## Screen Layout

### Left section: Node identity
- Node Name
- IP Address
- SSH Port
- Region / Label
- OS Type
- Tags

### Middle section: Access method
- Username
- Authentication Type
  - SSH Key
  - One-Time Password
  - Bootstrap Token
- SSH Key Reference or Secret Reference
- Verify Host Key toggle
- Expected Host Fingerprint (optional but recommended)

### Right section: Deployment options
- Install Profile
- Firewall Profile
- Agent Group
- Max Browser Sessions
- Enable Browser Runtime
- Auto-start Agent after install
- Notes

## Validation Rules
- Node Name: required, unique, 3 to 80 chars
- IP Address: required, valid IPv4 or IPv6
- SSH Port: required, numeric, 1 to 65535
- Username: required
- Authentication Type: required
- Install Profile: required
- Firewall Profile: required
- Max Browser Sessions: integer, 1 to configured upper bound

## Buttons
- `Test SSH`
- `Run Preflight`
- `Save Draft`
- `Install Agent`
- `Cancel`

## User Flow
1. Operator enters base node information.
2. Operator chooses auth method.
3. Operator clicks `Test SSH`.
4. If successful, operator clicks `Run Preflight`.
5. Preflight reports OS, disk, memory, service manager, and firewall readiness.
6. Operator chooses install profile and firewall profile.
7. Operator clicks `Install Agent`.
8. UI creates install job and moves to progress state.

## Progress Drawer
Display live installation steps:
- Connection established
- Host key verified
- OS detected
- Prerequisites checked
- Agent package uploaded/downloaded
- Config generated
- Service installed
- Firewall profile applied
- Agent started
- Enrollment succeeded

## Failure States
### SSH failure
Show:
- Could not connect
- Auth rejected
- Host key mismatch
- Port closed

### Preflight failure
Show exact failed check:
- Unsupported OS
- Low disk
- Low memory
- Missing service manager
- No write permission to install path

### Enrollment failure
Show:
- Token expired
- Backend unreachable
- Invalid capability registration

## UX Notes
- Never display raw secrets after save.
- Redact key material in logs and UI.
- Use inline badges for:
  - Draft
  - Verified
  - Preflight Passed
  - Installing
  - Online
  - Failed
- Provide copyable diagnostic IDs, not sensitive output.

## ViewModel Example
```json
{
  "nodeName": "vps-fr-01",
  "ipAddress": "145.83.10.2",
  "sshPort": 22,
  "region": "france",
  "osType": "ubuntu-22.04",
  "username": "nodeadmin",
  "authenticationType": "ssh_key",
  "secretReference": "secret://infra/vps-fr-01/ssh-key",
  "verifyHostKey": true,
  "expectedFingerprint": "SHA256:REPLACE_ME",
  "installProfile": "linux-browser-node-v1",
  "firewallProfile": "minimal-node-v1",
  "agentGroup": "booking-workers",
  "maxBrowserSessions": 50,
  "enableBrowserRuntime": true,
  "autoStartAgent": true,
  "notes": "Primary France worker"
}
```
