# Integration Alignment

## Domain Alignment
The account alert feature is now represented by real domain objects:
- `Account`
- `AccountWorkflowStage`
- `AccountAlert`
- `AccountStatus`
- `WorkflowStageState`
- `AlertSeverity`

## Infrastructure Alignment
Seed data now creates:
- 3 nodes
- 3 accounts
- full workflow stage timelines
- active alerts on selected stages

## API Alignment
Controllers now expose the same account-stage model that the desktop uses.

## Desktop Alignment
Desktop loads nodes and accounts from the API when available, and falls back to the same demo topology when the API is offline.
