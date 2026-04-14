namespace FleetManager.Desktop.Models;

public sealed class DesktopNodeRegistrySnapshot
{
    public int Version { get; set; } = 1;
    public List<DesktopManagedNodeRecord> Nodes { get; set; } = new();
}
