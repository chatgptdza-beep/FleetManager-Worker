namespace FleetManager.Domain.Enums;

public enum CommandStatus
{
    Pending = 0,
    Dispatched = 1,
    Executed = 2,
    Failed = 3,
    TimedOut = 4
}
