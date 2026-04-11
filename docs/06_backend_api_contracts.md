# Backend API Contracts

## Nodes

### `GET /api/nodes`
Returns all VPS nodes with health and account counters.

Example response:
```json
[
  {
    "id": "guid",
    "name": "VPS-PAR-01",
    "ipAddress": "10.0.0.21",
    "status": "Online",
    "lastHeartbeatAtUtc": "2026-04-10T16:00:00Z",
    "cpuPercent": 28,
    "ramPercent": 61,
    "diskPercent": 47,
    "activeSessions": 1,
    "runningAccounts": 1,
    "manualAccounts": 0,
    "alertAccounts": 1
  }
]
```

### `GET /api/nodes/{nodeId}`
Returns a single node summary.

### `POST /api/nodes`
Creates a node.

Example request:
```json
{
  "name": "VPS-PAR-01",
  "ipAddress": "10.0.0.21",
  "sshPort": 22,
  "sshUsername": "deploy",
  "authType": "SshKey",
  "osType": "Ubuntu",
  "region": "Paris"
}
```

### `POST /api/nodes/{nodeId}/commands`
Queues an allowlisted command.

## Accounts

### `GET /api/accounts`
Returns account summaries.

Optional query string:
- `nodeId` - filters accounts to the selected node.

Example response:
```json
[
  {
    "id": "guid",
    "email": "booking.alpha@example.com",
    "status": "Running",
    "nodeId": "guid",
    "nodeName": "VPS-PAR-01",
    "currentStage": "Proxy Check",
    "activeAlertSeverity": "Warning",
    "activeAlertStage": "Proxy Check",
    "activeAlertTitle": "Latency spike detected",
    "activeAlertMessage": "Proxy validation exceeded the threshold."
  }
]
```

### `GET /api/accounts/{accountId}/stage-alerts`
Returns stage timeline details for the selected account.

Example response:
```json
{
  "accountId": "guid",
  "email": "booking.bravo@example.com",
  "status": "Manual",
  "nodeId": "guid",
  "nodeName": "VPS-FRA-03",
  "currentStage": "Captcha Solve",
  "activeAlertSeverity": "Critical",
  "activeAlertStage": "Captcha Solve",
  "activeAlertTitle": "Stage failed after retries",
  "activeAlertMessage": "Manual review is required.",
  "stages": [
    {
      "stageCode": "captcha_solve",
      "stageName": "Captcha Solve",
      "state": "Failed",
      "message": "External solver timeout on three attempts.",
      "occurredAtUtc": "2026-04-10T16:20:00Z"
    }
  ]
}
```

## Agent

### `POST /api/agent/heartbeat`
Updates node metrics and liveness.

Example request:
```json
{
  "nodeId": "guid",
  "agentVersion": "1.0.0",
  "cpuPercent": 27,
  "ramPercent": 58,
  "diskPercent": 45,
  "activeSessions": 1
}
```

## SignalR

### `/hubs/operations`
Reserved for live node/account/alert updates.
