namespace FleetManager.Contracts.Agent;

public sealed class AgentCommandCompletionRequest
{
    public Guid NodeId { get; set; }
    public bool Succeeded { get; set; }
    public string? ResultMessage { get; set; }
}
