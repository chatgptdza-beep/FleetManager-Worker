namespace FleetManager.Desktop.Models;

public sealed class DesktopManagedNodeRecord
{
    public Guid WorkflowNodeId { get; set; } = Guid.NewGuid();
    public Guid? RemoteNodeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string CurrentIp { get; set; } = string.Empty;
    public int SshPort { get; set; } = 22;
    public string SshUsername { get; set; } = "root";
    public string AuthType { get; set; } = "Password";
    public string? EncryptedSshPassword { get; set; }
    public string? EncryptedSshPrivateKey { get; set; }
    public int LocalPort { get; set; }
    public int ControlPort { get; set; } = 9001;
    public string OsType { get; set; } = "Ubuntu";
    public string? Region { get; set; }
    public DesktopManagedNodeStatus Status { get; set; } = DesktopManagedNodeStatus.Unknown;
    public string? StatusMessage { get; set; }
    public string? LastKnownApiBaseUrl { get; set; }
    public DateTime? LastHealthCheckAtUtc { get; set; }
    public DateTime? LastSshSuccessAtUtc { get; set; }
    public DateTime? LastHealedAtUtc { get; set; }
    public DateTime? LastTaskSyncAtUtc { get; set; }
    public List<DesktopManagedAccountTask> TaskData { get; set; } = new();

    public bool HasStoredCredentials
        => !string.IsNullOrWhiteSpace(EncryptedSshPassword) || !string.IsNullOrWhiteSpace(EncryptedSshPrivateKey);
}
