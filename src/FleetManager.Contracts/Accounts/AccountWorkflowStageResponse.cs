namespace FleetManager.Contracts.Accounts;

public sealed class AccountWorkflowStageResponse
{
    public string StageCode { get; set; } = string.Empty;
    public string StageName { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string? Message { get; set; }
    public DateTime? OccurredAtUtc { get; set; }
}
