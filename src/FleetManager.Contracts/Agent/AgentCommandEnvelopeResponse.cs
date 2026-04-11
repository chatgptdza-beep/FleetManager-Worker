namespace FleetManager.Contracts.Agent;

public sealed class AgentCommandEnvelopeResponse
{
    public Guid CommandId { get; set; }
    public Guid NodeId { get; set; }
    public string CommandType { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
    public DateTime CreatedAtUtc { get; set; }
}
