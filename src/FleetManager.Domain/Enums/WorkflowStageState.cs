namespace FleetManager.Domain.Enums;

public enum WorkflowStageState
{
    Pending = 0,
    Running = 1,
    Completed = 2,
    Warning = 3,
    Failed = 4,
    ManualRequired = 5
}
