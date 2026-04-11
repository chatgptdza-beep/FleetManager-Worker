# VPS Management Package

This package contains a secure, production-oriented specification set for a VPS management platform with automated node onboarding, agent installation, browser/session orchestration, logging, alerts, and operator controls.

## Files
- `01_secure_vps_onboarding_spec.md` - master specification
- `02_add_vps_screen_spec.md` - Add VPS screen fields, states, validation, and UX flows
- `03_install_job_state_machine.md` - onboarding/install state machine and retry logic
- `04_agent_config_schema.yaml` - agent configuration schema and example values
- `05_firewall_profile_schema.yaml` - firewall profile schema and example minimal policy
- `06_backend_api_contracts.md` - backend API routes, DTOs, and sample payloads
- `07_database_erd.md` - Mermaid ERD and table notes
- `08_operator_permissions_matrix.md` - roles and permissions matrix
- `09_solution_structure.md` - suggested .NET solution structure and responsibilities
- `10_install_profiles_and_bootstrap.md` - install profiles, bootstrap plan, and rollout notes

## Recommended implementation order
1. Database + enums
2. Backend API + auth
3. Install jobs + node registration
4. Agent heartbeat + metrics
5. Dashboard + Add VPS flow
6. Browser/session controls
7. Alerts + manual queue
8. Audit logs + permissions hardening

## Suggested stack
- Desktop UI: WPF
- Backend: ASP.NET Core
- Database: PostgreSQL
- Cache / queue: Redis
- Agent: .NET Worker Service
- Realtime: SignalR
- Logging: Serilog
