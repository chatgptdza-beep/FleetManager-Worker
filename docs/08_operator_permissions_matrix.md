# Operator Permissions Matrix

## Roles
- `SuperAdmin`
- `Admin`
- `Operator`
- `Viewer`

## Matrix
| Permission | SuperAdmin | Admin | Operator | Viewer |
|---|---|---:|---:|---:|
| View dashboard | Yes | Yes | Yes | Yes |
| View node details | Yes | Yes | Yes | Yes |
| Add VPS node | Yes | Yes | No | No |
| Edit VPS metadata | Yes | Yes | No | No |
| Start install job | Yes | Yes | No | No |
| Retry failed install | Yes | Yes | No | No |
| Change firewall profile | Yes | Yes | No | No |
| View account table | Yes | Yes | Yes | Yes |
| Start session | Yes | Yes | Yes | No |
| Stop session | Yes | Yes | Yes | No |
| Restart browser worker | Yes | Yes | Yes | No |
| Bring browser to front | Yes | Yes | Yes | No |
| Capture screenshot | Yes | Yes | Yes | No |
| View logs | Yes | Yes | Yes | Yes |
| Acknowledge alert | Yes | Yes | Yes | No |
| Claim manual queue item | Yes | Yes | Yes | No |
| Resolve manual queue item | Yes | Yes | Yes | No |
| Update install profile | Yes | No | No | No |
| Update agent package channel | Yes | No | No | No |
| Manage users and roles | Yes | No | No | No |
| View audit logs | Yes | Yes | Limited | Read only |
| Export sensitive artifacts | Yes | Yes | Limited | No |

## Guardrails
- Destructive actions require confirmation.
- High-risk changes should require reason text.
- Role changes must be audited.
- Sensitive data exports should be watermarked and logged.
- Viewer role must never see secret references or operational secrets.

## Optional approval policy
For high-impact actions, require dual approval or step-up auth:
- changing firewall profile
- changing install profile version
- re-enrolling production nodes
- bulk restarting sessions
