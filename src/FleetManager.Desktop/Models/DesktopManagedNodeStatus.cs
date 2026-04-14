namespace FleetManager.Desktop.Models;

public enum DesktopManagedNodeStatus
{
    Unknown = 0,
    Connected = 1,
    RemoteReachable = 2,
    Installing = 3,
    ActionRequired = 4,
    Error = 5
}
