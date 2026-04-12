namespace FleetManager.Contracts.Accounts;

public sealed class AccountStageAlertDetailsResponse
{
    public Guid AccountId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public Guid NodeId { get; set; }
    public string NodeName { get; set; } = string.Empty;
    public string NodeIpAddress { get; set; } = string.Empty;
    public string CurrentStage { get; set; } = string.Empty;
    public string? ActiveAlertSeverity { get; set; }
    public string? ActiveAlertStage { get; set; }
    public string? ActiveAlertTitle { get; set; }
    public string? ActiveAlertMessage { get; set; }
    public int CurrentProxyIndex { get; set; }
    public int ProxyCount { get; set; }
    public IReadOnlyList<AccountProxyRotationResponse> ProxyRotations { get; set; } = Array.Empty<AccountProxyRotationResponse>();
    public IReadOnlyList<AccountWorkflowStageResponse> Stages { get; set; } = Array.Empty<AccountWorkflowStageResponse>();
}
