namespace FleetManager.Contracts.Nodes;

public sealed class NodeHeartbeatRequest
{
    public Guid? NodeId { get; set; }
    public string NodeName { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public double CpuPercent { get; set; }
    public double RamPercent { get; set; }
    public int PingMs { get; set; }
    public int ActiveSessionCount { get; set; }
    public int BrowserCount { get; set; }
    public DateTime OccurredAtUtc { get; set; }
}
