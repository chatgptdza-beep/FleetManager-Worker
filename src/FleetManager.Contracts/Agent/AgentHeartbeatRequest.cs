namespace FleetManager.Contracts.Agent;

public sealed class AgentHeartbeatRequest
{
    public Guid NodeId { get; set; }
    public string AgentVersion { get; set; } = string.Empty;
    public double CpuPercent { get; set; }
    public double RamPercent { get; set; }
    public double DiskPercent { get; set; }
    public double RamUsedGb { get; set; }
    public double StorageUsedGb { get; set; }
    public int PingMs { get; set; }
    public int ActiveSessions { get; set; }
    public int ControlPort { get; set; }
    public string ConnectionState { get; set; } = "Connected";
    public int ConnectionTimeoutSeconds { get; set; }
}
