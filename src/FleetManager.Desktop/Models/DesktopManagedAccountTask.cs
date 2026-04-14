namespace FleetManager.Desktop.Models;

public sealed class DesktopManagedAccountTask
{
    public Guid AccountId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string CurrentStage { get; set; } = string.Empty;
    public string? ActiveAlertTitle { get; set; }
    public string? ActiveAlertMessage { get; set; }
    public int CurrentProxyIndex { get; set; }
    public int ProxyCount { get; set; }
}
