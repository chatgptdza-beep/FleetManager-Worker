namespace FleetManager.Contracts.Nodes;

public sealed class NodeCommandStatusResponse
{
    public Guid CommandId { get; set; }
    public Guid NodeId { get; set; }
    public string CommandType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? ResultMessage { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public DateTime? ExecutedAtUtc { get; set; }
}
