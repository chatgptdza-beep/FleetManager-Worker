using FleetManager.Domain.Common;
using FleetManager.Domain.Enums;
using System.ComponentModel.DataAnnotations.Schema;

namespace FleetManager.Domain.Entities;

public sealed class VpsNode : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public int SshPort { get; set; } = 22;
    public string SshUsername { get; set; } = string.Empty;
    [NotMapped]
    public string? SshPassword { get; set; }
    [NotMapped]
    public string? SshPrivateKey { get; set; }
    public string AuthType { get; set; } = "SshKey";
    public string OsType { get; set; } = string.Empty;
    public string? Region { get; set; }
    public NodeStatus Status { get; set; } = NodeStatus.Pending;
    public DateTime? LastHeartbeatAtUtc { get; set; }
    public double CpuPercent { get; set; }
    public double RamPercent { get; set; }
    public double DiskPercent { get; set; }
    public double RamUsedGb { get; set; }
    public double StorageUsedGb { get; set; }
    public int PingMs { get; set; }
    public int ActiveSessions { get; set; }
    public int ControlPort { get; set; } = 9001;
    public string ConnectionState { get; set; } = "Connected";
    public int ConnectionTimeoutSeconds { get; set; } = 5;
    public string? AgentVersion { get; set; }

    public ICollection<AgentInstallJob> InstallJobs { get; set; } = new List<AgentInstallJob>();
    public ICollection<NodeCommand> Commands { get; set; } = new List<NodeCommand>();
    public ICollection<Account> Accounts { get; set; } = new List<Account>();
}
