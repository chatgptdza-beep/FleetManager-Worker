# Account Stage Alert Feature

## Goal
When the operator clicks an account row, the UI should immediately reveal whether a specific workflow stage has a problem.

## UX Behavior
- The account row may show a small colored indicator if an active issue exists.
- Selecting a VPS filters the accounts displayed.
- When the account is selected, the detail panel displays:
  - current stage,
  - alert severity,
  - alert title,
  - alert message,
  - colored workflow stage timeline.

## Severity Color Rules
- `Warning` -> amber/yellow
- `Critical` -> red
- `ManualRequired` -> orange
- `Completed` -> green
- `Running` -> blue
- `Pending` -> gray

## API Support
- `GET /api/accounts`
- `GET /api/accounts?nodeId={nodeId}`
- `GET /api/accounts/{accountId}/stage-alerts`

## Data Flow
1. Desktop fetches nodes.
2. Selecting a node fetches filtered account summaries.
3. Selecting an account fetches stage details.
4. Stage rows derive their colors from the stage state.

## Future Improvements
- acknowledge alert action
- retry stage action
- open session logs at failing stage
- capture screenshot of failing stage
- filter accounts by severity
