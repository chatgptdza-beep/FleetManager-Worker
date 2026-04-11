using FleetManager.Domain.Common;

namespace FleetManager.Domain.Entities;

public sealed class NodeCapability : BaseEntity
{
    public Guid VpsNodeId { get; set; }
    public bool CanStartBrowser { get; set; }
    public bool CanStopBrowser { get; set; }
    public bool CanRestartBrowser { get; set; }
    public bool CanCaptureScreenshot { get; set; }
    public bool CanFetchLogs { get; set; }
    public bool CanUpdateAgent { get; set; }
}
