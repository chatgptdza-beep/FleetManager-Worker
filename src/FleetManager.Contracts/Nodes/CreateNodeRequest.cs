namespace FleetManager.Contracts.Nodes;

public sealed class CreateNodeRequest
{
    public string Name { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public int SshPort { get; set; } = 22;
    public int ControlPort { get; set; } = 9001;
    public string SshUsername { get; set; } = string.Empty;
    public string? SshPassword { get; set; }
    public string? SshPrivateKey { get; set; }
    public string AuthType { get; set; } = "SshKey";
    public string OsType { get; set; } = "Ubuntu";
    public string? Region { get; set; }
}
