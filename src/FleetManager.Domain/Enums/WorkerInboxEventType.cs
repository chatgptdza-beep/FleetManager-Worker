namespace FleetManager.Domain.Enums;

public enum WorkerInboxEventType
{
    ManualTakeoverRequired = 0,
    ManualTakeoverRequested = 1,
    ProxyRotated = 2,
    CommandFailed = 3
}
