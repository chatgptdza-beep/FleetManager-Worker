using System.Windows.Media;
using FleetManager.Contracts.Accounts;
using FleetManager.Desktop.Styling;

namespace FleetManager.Desktop.ViewModels;

public sealed class AccountStageViewModel
{
    public string StageName { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
    public string TimeLabel { get; init; } = string.Empty;

    public Brush BadgeBackground => State switch
    {
        "Completed" => UiPalette.SuccessBackground,
        "Running" => UiPalette.InfoBackground,
        "Warning" => UiPalette.WarningBackground,
        "Failed" => UiPalette.CriticalBackground,
        "ManualRequired" => UiPalette.ManualBackground,
        "Pending" => UiPalette.PendingBackground,
        _ => UiPalette.NeutralBadgeBackground
    };

    public Brush BadgeForeground => State switch
    {
        "Completed" => UiPalette.SuccessForeground,
        "Running" => UiPalette.InfoForeground,
        "Warning" => UiPalette.WarningForeground,
        "Failed" => UiPalette.CriticalForeground,
        "ManualRequired" => UiPalette.ManualForeground,
        "Pending" => UiPalette.PendingForeground,
        _ => UiPalette.NeutralBadgeForeground
    };

    public Brush CardBackground => State switch
    {
        "Failed" => UiPalette.CriticalBackground,
        "Warning" => UiPalette.WarningBackground,
        "ManualRequired" => UiPalette.ManualBackground,
        _ => UiPalette.SoftCardBackground
    };

    public Brush CardBorderBrush => State switch
    {
        "Failed" => UiPalette.CriticalForeground,
        "Warning" => UiPalette.WarningForeground,
        "ManualRequired" => UiPalette.ManualForeground,
        _ => UiPalette.CardBorder
    };

    public static AccountStageViewModel FromContract(AccountWorkflowStageResponse response) => new()
    {
        StageName = response.StageName,
        State = response.State,
        Detail = response.Message ?? string.Empty,
        TimeLabel = response.OccurredAtUtc.HasValue ? response.OccurredAtUtc.Value.ToLocalTime().ToString("g") : "Not started"
    };
}
