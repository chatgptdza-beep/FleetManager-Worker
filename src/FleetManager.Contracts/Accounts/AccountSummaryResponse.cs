namespace FleetManager.Contracts.Accounts;

public sealed class AccountSummaryResponse
{
    public Guid Id { get; set; }
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
}
