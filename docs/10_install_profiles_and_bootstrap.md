# Install Profiles and Bootstrap Plan

## Install Profile Object
Each install profile should define:
- target OS families
- required packages
- agent package source
- browser runtime requirement
- service definition template
- default config template
- firewall profile default
- capability set
- rollback hints

## Example Profiles
### `linux-browser-node-v1`
- Ubuntu 22.04 and Debian 12
- browser runtime enabled
- max sessions configurable
- local-only debug ports
- stable agent channel

### `linux-lite-node-v1`
- no browser runtime
- telemetry only
- reduced permissions

## Bootstrap Sequence
1. Connect via approved admin user.
2. Verify host key.
3. Render install profile.
4. Upload or download signed agent package.
5. Create runtime directories.
6. Write config file from schema.
7. Install service.
8. Apply firewall profile.
9. Start service.
10. Enroll and verify heartbeat.

## Rollout Strategy
- Use canary group first.
- Promote only after heartbeat stability window passes.
- Version profiles immutably.
- Keep previous working profile available for rollback.

## Operational Notes
- Keep bootstrap secrets short-lived.
- Prefer package signatures.
- Keep profile rendering deterministic.
- Log every install action with correlation ID.
