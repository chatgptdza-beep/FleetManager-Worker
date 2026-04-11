# Database ERD

## Mermaid ERD
```mermaid
erDiagram
    USERS ||--o{ AUDIT_LOGS : creates
    VPS_NODES ||--o{ VPS_CREDENTIALS : has
    VPS_NODES ||--o{ AGENT_INSTALL_JOBS : has
    VPS_NODES ||--|| NODE_CAPABILITIES : exposes
    VPS_NODES ||--o{ HEARTBEATS : emits
    VPS_NODES ||--o{ NODE_COMMANDS : receives
    VPS_NODES ||--o{ ACCOUNTS : assigned
    ACCOUNTS ||--o{ SESSIONS : runs
    SESSIONS ||--o{ SESSION_LOGS : produces
    SESSIONS ||--o{ ALERTS : raises
    SESSIONS ||--o{ MANUAL_QUEUE : queues
    FIREWALL_PROFILES ||--o{ VPS_NODES : assigned
    INSTALL_PROFILES ||--o{ VPS_NODES : assigned
    PROXIES ||--o{ ACCOUNTS : assigned
    PROXIES ||--o{ SESSIONS : used_by

    USERS {
        string id PK
        string email
        string role
        boolean is_active
        datetime created_at
    }
    VPS_NODES {
        string id PK
        string name
        string ip_address
        int ssh_port
        string ssh_username
        string auth_type
        string os_type
        string status
        string install_profile_id FK
        string firewall_profile_id FK
        datetime last_heartbeat_at
    }
    VPS_CREDENTIALS {
        string id PK
        string vps_node_id FK
        string credential_type
        string secret_reference
        boolean is_temporary
        datetime expires_at
    }
    AGENT_INSTALL_JOBS {
        string id PK
        string vps_node_id FK
        string job_status
        string current_step
        int attempt
        datetime started_at
        datetime ended_at
        string error_message
    }
    NODE_CAPABILITIES {
        string vps_node_id PK
        boolean can_start_browser
        boolean can_stop_browser
        boolean can_restart_browser
        boolean can_capture_screenshot
        boolean can_fetch_logs
        boolean can_update_agent
    }
    HEARTBEATS {
        string id PK
        string vps_node_id FK
        int cpu_percent
        int ram_percent
        int ping_ms
        int browser_count
        int active_sessions
        datetime created_at
    }
    ACCOUNTS {
        string id PK
        string email
        string account_status
        string assigned_vps_id FK
        string assigned_proxy_id FK
        int priority
        datetime updated_at
    }
    PROXIES {
        string id PK
        string proxy_host
        int proxy_port
        string status
        datetime last_checked_at
    }
    SESSIONS {
        string id PK
        string account_id FK
        string vps_node_id FK
        string proxy_id FK
        string session_status
        int browser_port
        datetime started_at
        datetime ended_at
    }
    SESSION_LOGS {
        string id PK
        string session_id FK
        string level
        string source
        string message
        datetime created_at
    }
    ALERTS {
        string id PK
        string session_id FK
        string alert_type
        string severity
        string message
        boolean is_acknowledged
        datetime created_at
    }
    MANUAL_QUEUE {
        string id PK
        string session_id FK
        string reason_code
        string queue_status
        string assigned_to_user_id FK
        datetime created_at
        datetime resolved_at
    }
    NODE_COMMANDS {
        string id PK
        string vps_node_id FK
        string session_id FK
        string command_type
        string status
        string requested_by_user_id FK
        datetime created_at
        datetime executed_at
    }
    FIREWALL_PROFILES {
        string id PK
        string name
        string version
        datetime created_at
    }
    INSTALL_PROFILES {
        string id PK
        string name
        string version
        datetime created_at
    }
    AUDIT_LOGS {
        string id PK
        string actor_user_id FK
        string action_type
        string target_type
        string target_id
        string result_status
        datetime created_at
    }
```

## Notes
- Store passwords and secrets by reference, not plaintext.
- Use enums or constrained lookup tables for statuses.
- Partition `session_logs` and `heartbeats` if volume grows.
- `node_capabilities` can be a 1:1 table or a JSON column if flexibility is preferred.

## Suggested indexes
- `vps_nodes(status, last_heartbeat_at)`
- `accounts(assigned_vps_id, account_status)`
- `sessions(vps_node_id, session_status, started_at)`
- `alerts(is_acknowledged, severity, created_at)`
- `manual_queue(queue_status, priority, created_at)`
- `node_commands(vps_node_id, status, created_at)`
- `session_logs(session_id, created_at)`
- `heartbeats(vps_node_id, created_at)`
