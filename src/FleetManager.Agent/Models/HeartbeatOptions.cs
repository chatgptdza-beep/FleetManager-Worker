namespace FleetManager.Agent.Models;

public sealed class HeartbeatOptions
{
    public const string SectionName = "Heartbeat";

    public Guid? NodeId { get; set; }
    public string NodeName { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public int IntervalSeconds { get; set; } = 15;
    public string ApiBaseUrl { get; set; } = string.Empty;
}
