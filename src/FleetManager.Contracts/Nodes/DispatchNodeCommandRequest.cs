namespace FleetManager.Contracts.Nodes;

public sealed class DispatchNodeCommandRequest
{
    public string CommandType { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
}
