namespace FleetManager.Contracts.Nodes;

public sealed class UpdateNodeStatusRequest
{
    public string Status { get; set; } = string.Empty;
}
