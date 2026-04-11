namespace FleetManager.Domain.Enums;

public enum NodeStatus
{
    Pending = 0,
    Installing = 1,
    Online = 2,
    Degraded = 3,
    Offline = 4,
    InstallFailed = 5,
    Maintenance = 6
}
