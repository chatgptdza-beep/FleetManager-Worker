namespace FleetManager.Contracts.Nodes;

public sealed class NodeSummaryResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public int SshPort { get; set; }
    public string SshUsername { get; set; } = string.Empty;
    public string AuthType { get; set; } = string.Empty;
    public string OsType { get; set; } = string.Empty;
    public string? Region { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime? LastHeartbeatAtUtc { get; set; }
    public double CpuPercent { get; set; }
    public double RamPercent { get; set; }
    public double DiskPercent { get; set; }
    public double RamUsedGb { get; set; }
    public double StorageUsedGb { get; set; }
    public int PingMs { get; set; }
    public int ActiveSessions { get; set; }
    public int ControlPort { get; set; }
    public string ConnectionState { get; set; } = string.Empty;
    public int ConnectionTimeoutSeconds { get; set; }
    public int AssignedAccountCount { get; set; }
    public int RunningAccounts { get; set; }
    public int ManualAccounts { get; set; }
    public int AlertAccounts { get; set; }
    public string? AgentVersion { get; set; }
}
