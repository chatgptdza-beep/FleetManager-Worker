namespace FleetManager.Agent;

public class AgentSettings
{
    public const string SectionName = "Agent";

    public string NodeId { get; set; } = string.Empty;
    public string BackendBaseUrl { get; set; } = string.Empty;
    public string AgentVersion { get; set; } = "1.0.0";
    public int HeartbeatIntervalSeconds { get; set; } = 30;
}
