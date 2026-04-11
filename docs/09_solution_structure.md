# Suggested Solution Structure

## Projects
- `FleetManager.Desktop`
- `FleetManager.Api`
- `FleetManager.Application`
- `FleetManager.Domain`
- `FleetManager.Infrastructure`
- `FleetManager.Agent`
- `FleetManager.Contracts`
- `FleetManager.SharedKernel`

## Responsibilities
### FleetManager.Desktop
- WPF UI
- SignalR client
- Add VPS flow
- Dashboard
- Manual queue
- Logs viewer

### FleetManager.Api
- REST API
- auth and authorization
- SignalR hubs
- install orchestration endpoints
- node command endpoints

### FleetManager.Application
- use cases
- command handlers
- validation
- install job orchestrators
- permission checks

### FleetManager.Domain
- entities
- aggregates
- value objects
- enums
- domain events

### FleetManager.Infrastructure
- EF Core
- PostgreSQL repositories
- Redis queues/cache
- secrets provider integration
- background services

### FleetManager.Agent
- node worker service
- heartbeat
- browser/session manager
- command executor
- log shipper

### FleetManager.Contracts
- DTOs
- API contracts
- shared event payloads

## Suggested folders in API
```text
FleetManager.Api/
  Controllers/
  Hubs/
  Middleware/
  Authentication/
  Authorization/
  Program.cs
```

## Suggested folders in Application
```text
FleetManager.Application/
  Nodes/
    Commands/
    Queries/
    Validators/
  InstallJobs/
  Sessions/
  Alerts/
  ManualQueue/
  Audit/
```

## Suggested folders in Agent
```text
FleetManager.Agent/
  Heartbeat/
  Commands/
  Browser/
  Sessions/
  Logging/
  Security/
  Configuration/
```
