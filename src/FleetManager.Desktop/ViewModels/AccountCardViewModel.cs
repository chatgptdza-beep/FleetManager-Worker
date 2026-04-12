using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using FleetManager.Contracts.Accounts;
using FleetManager.Desktop.Styling;

namespace FleetManager.Desktop.ViewModels;

public sealed class AccountCardViewModel : ViewModelBase
{
    private bool _isChecked;

    public bool IsChecked
    {
        get => _isChecked;
        set => SetProperty(ref _isChecked, value);
    }
    public Guid AccountId { get; init; }
    public Guid NodeId { get; init; }
    public string Email { get; init; } = string.Empty;
    public string Username { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string NodeName { get; init; } = string.Empty;
    public string NodeIpAddress { get; init; } = string.Empty;
    public string CurrentStage { get; init; } = string.Empty;
    public string? ActiveAlertSeverity { get; init; }
    public string? ActiveAlertStage { get; init; }
    public string? ActiveAlertTitle { get; init; }
    public string? ActiveAlertMessage { get; init; }
    public int CurrentProxyIndex { get; init; }
    public int ProxyCount { get; init; }
    public ObservableCollection<AccountProxyRotationViewModel> ProxyRotations { get; init; } = new();
    public ObservableCollection<AccountStageViewModel> Stages { get; init; } = new();

    public string DisplayName => Email;
    public string SecondaryIdentity => string.IsNullOrWhiteSpace(Username) ? "No username" : Username;
    public string AccountCode => AccountId.ToString("N")[..6].ToUpperInvariant();
    public string NodeDisplayName => $"{NodeName} ({NodeIpAddress})";
    public string ProxyIndexLabel => ProxyCount <= 0
        ? "No proxies"
        : $"{Math.Min(CurrentProxyIndex + 1, ProxyCount)} / {ProxyCount}";
    public string ProxySummary => ProxyCount <= 0
        ? "No proxy pool assigned to this account."
        : $"Current slot {Math.Min(CurrentProxyIndex + 1, ProxyCount)} out of {ProxyCount} configured proxies.";
    public bool RequiresManualAttention => Status is "Manual" or "Paused"
        || string.Equals(ActiveAlertSeverity, "ManualRequired", StringComparison.OrdinalIgnoreCase);
    public bool HasIssue => RequiresManualAttention || !string.IsNullOrWhiteSpace(ActiveAlertTitle);

    public string StatusSummary => $"{NodeName} | Stage: {CurrentStage} | Proxy: {ProxyIndexLabel}";
    public string DetailSummary => $"{Status} | {NodeName} | {NodeIpAddress} | Current stage: {CurrentStage} | Proxy: {ProxyIndexLabel}";
    public string ActiveAlertSummary => !string.IsNullOrWhiteSpace(ActiveAlertTitle)
        ? $"{ActiveAlertStage}: {ActiveAlertTitle}"
        : "No active issue";
    public string AlertHeader => HasIssue
        ? $"{ActiveAlertSeverity ?? Status} | {ActiveAlertStage ?? CurrentStage}"
        : "Workflow healthy";
    public string AlertMessage => HasIssue
        ? ActiveAlertMessage ?? RecentLogSummary
        : "The selected account has no active stage issue.";
    public Visibility AlertBannerVisibility => HasIssue ? Visibility.Visible : Visibility.Collapsed;
    public string RecentLogSummary => !string.IsNullOrWhiteSpace(ActiveAlertTitle)
        ? ActiveAlertTitle
        : Status switch
        {
            "Manual" => "Booking page reached and waiting for operator takeover",
            "Paused" => "Waiting for manual confirmation before resume",
            "Error" => "Worker reported a recoverable failure",
            _ => $"{CurrentStage} running without active alerts"
        };

    public string ActionLabel => PrimaryCommandType switch
    {
        "OpenAssignedSession" => "View Browser",
        "StartBrowser" => "Start",
        _ => "Open Logs"
    };

    public string PrimaryCommandType => RequiresManualAttention
        ? "OpenAssignedSession"
        : Status is "Paused" or "Error"
            ? "StartBrowser"
            : "FetchSessionLogs";

    public Brush StatusBadgeBackground => Status switch
    {
        "Stable" => UiPalette.SuccessBackground,
        "Stopped" => UiPalette.SuccessBackground,
        "Running" => UiPalette.InfoBackground,
        "Manual" => UiPalette.CriticalBackground,
        "Paused" => UiPalette.WarningBackground,
        "Error" => UiPalette.CriticalBackground,
        _ => UiPalette.NeutralBadgeBackground
    };

    public Brush StatusBadgeForeground => Status switch
    {
        "Stable" => UiPalette.SuccessForeground,
        "Stopped" => UiPalette.SuccessForeground,
        "Running" => UiPalette.InfoForeground,
        "Manual" => UiPalette.CriticalForeground,
        "Paused" => UiPalette.WarningForeground,
        "Error" => UiPalette.CriticalForeground,
        _ => UiPalette.NeutralBadgeForeground
    };

    public Brush AlertBackground => ActiveAlertSeverity switch
    {
        "Critical" => UiPalette.CriticalBackground,
        "Warning" => UiPalette.WarningBackground,
        "ManualRequired" => UiPalette.ManualBackground,
        "Info" => UiPalette.InfoBackground,
        _ => UiPalette.PanelBackgroundAlt
    };

    public Brush AlertBorderBrush => ActiveAlertSeverity switch
    {
        "Critical" => UiPalette.CriticalForeground,
        "Warning" => UiPalette.WarningForeground,
        "ManualRequired" => UiPalette.ManualForeground,
        "Info" => UiPalette.InfoForeground,
        _ => UiPalette.CardBorder
    };

    public Brush AlertForeground => ActiveAlertSeverity switch
    {
        "Critical" => UiPalette.CriticalForeground,
        "Warning" => UiPalette.WarningForeground,
        "ManualRequired" => UiPalette.ManualForeground,
        "Info" => UiPalette.InfoForeground,
        _ => UiPalette.NeutralText
    };

    public Brush IssueDotBrush => HasIssue ? AlertBorderBrush : UiPalette.CardBorder;
    public Brush RowBackground => RequiresManualAttention
        ? UiPalette.CriticalBackground
        : HasIssue
            ? UiPalette.WarningBackground
            : UiPalette.PanelBackground;

    public Brush RowBorderBrush => RequiresManualAttention
        ? UiPalette.CriticalForeground
        : HasIssue
            ? UiPalette.WarningForeground
            : UiPalette.CardBorder;

    public Brush ActionBackground => PrimaryCommandType switch
    {
        "OpenAssignedSession" => UiPalette.CriticalBackground,
        "StartBrowser" => UiPalette.Accent,
        _ => UiPalette.PanelBackgroundAlt
    };

    public Brush ActionForeground => PrimaryCommandType switch
    {
        "OpenAssignedSession" => UiPalette.CriticalForeground,
        "StartBrowser" => UiPalette.AccentForeground,
        _ => UiPalette.Accent
    };

    public Brush ActionBorderBrush => PrimaryCommandType switch
    {
        "OpenAssignedSession" => UiPalette.CriticalForeground,
        "StartBrowser" => UiPalette.Accent,
        _ => UiPalette.CardBorder
    };

    public static AccountCardViewModel FromSummary(AccountSummaryResponse response) => new()
    {
        AccountId = response.Id,
        NodeId = response.NodeId,
        Email = response.Email,
        Username = response.Username,
        Status = response.Status,
        NodeName = response.NodeName,
        NodeIpAddress = response.NodeIpAddress,
        CurrentStage = response.CurrentStage,
        ActiveAlertSeverity = response.ActiveAlertSeverity,
        ActiveAlertStage = response.ActiveAlertStage,
        ActiveAlertTitle = response.ActiveAlertTitle,
        ActiveAlertMessage = response.ActiveAlertMessage,
        CurrentProxyIndex = response.CurrentProxyIndex,
        ProxyCount = response.ProxyCount
    };

    public static AccountCardViewModel FromDetails(AccountStageAlertDetailsResponse response) => new()
    {
        AccountId = response.AccountId,
        NodeId = response.NodeId,
        Email = response.Email,
        Username = response.Username,
        Status = response.Status,
        NodeName = response.NodeName,
        NodeIpAddress = response.NodeIpAddress,
        CurrentStage = response.CurrentStage,
        ActiveAlertSeverity = response.ActiveAlertSeverity,
        ActiveAlertStage = response.ActiveAlertStage,
        ActiveAlertTitle = response.ActiveAlertTitle,
        ActiveAlertMessage = response.ActiveAlertMessage,
        CurrentProxyIndex = response.CurrentProxyIndex,
        ProxyCount = response.ProxyCount,
        ProxyRotations = new ObservableCollection<AccountProxyRotationViewModel>(response.ProxyRotations.Select(AccountProxyRotationViewModel.FromContract)),
        Stages = new ObservableCollection<AccountStageViewModel>(response.Stages.Select(AccountStageViewModel.FromContract))
    };
}
