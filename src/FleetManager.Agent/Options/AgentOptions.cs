namespace FleetManager.Agent.Options;

public sealed class AgentOptions
{
    public const string SectionName = "Agent";

    public Guid NodeId { get; set; }
    public string BackendBaseUrl { get; set; } = string.Empty;
    public int HeartbeatIntervalSeconds { get; set; } = 15;
    public int CommandPollIntervalSeconds { get; set; } = 3;
    public string AgentVersion { get; set; } = "0.1.0";
    public int ControlPort { get; set; } = 9001;
    public string ConnectionState { get; set; } = "Connected";
    public int ConnectionTimeoutSeconds { get; set; } = 5;
    public string CommandScriptsPath { get; set; } = "/opt/fleetmanager-agent/commands";
    public int CommandTimeoutMinutes { get; set; } = 5;
    
    public string ApiKey { get; set; } = string.Empty;
    public string NodeIpAddress { get; set; } = "127.0.0.1";
    public string NodeName { get; set; } = string.Empty;
    public bool EnableDockerMonitor { get; set; } = false;

    // Unpacked extension directories to load on every StartBrowser command for this VPS.
    public string[] BrowserExtensions { get; set; } = Array.Empty<string>();
}
