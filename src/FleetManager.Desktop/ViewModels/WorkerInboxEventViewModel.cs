using System.Windows;
using System.Windows.Media;
using FleetManager.Contracts.Operations;
using FleetManager.Desktop.Styling;

namespace FleetManager.Desktop.ViewModels;

public sealed class WorkerInboxEventViewModel
{
    public Guid EventId { get; init; }
    public string EventType { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string? ActionUrl { get; init; }
    public Guid? AccountId { get; init; }
    public string? AccountEmail { get; init; }
    public Guid? NodeId { get; init; }
    public string? NodeName { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime? UpdatedAtUtc { get; init; }
    public DateTime? AcknowledgedAtUtc { get; init; }

    public string BadgeText => EventType switch
    {
        "ManualTakeoverRequired" => "Manual",
        "ManualTakeoverRequested" => "Takeover",
        "ProxyRotated" => "Proxy",
        "CommandFailed" => "Failure",
        _ => "Worker"
    };

    public Brush BadgeBackground => EventType switch
    {
        "ManualTakeoverRequired" => UiPalette.ManualBackground,
        "ManualTakeoverRequested" => UiPalette.WarningBackground,
        "ProxyRotated" => UiPalette.WarningBackground,
        "CommandFailed" => UiPalette.CriticalBackground,
        _ => UiPalette.NeutralBadgeBackground
    };

    public Brush BadgeForeground => EventType switch
    {
        "ManualTakeoverRequired" => UiPalette.ManualForeground,
        "ManualTakeoverRequested" => UiPalette.WarningForeground,
        "ProxyRotated" => UiPalette.WarningForeground,
        "CommandFailed" => UiPalette.CriticalForeground,
        _ => UiPalette.NeutralBadgeForeground
    };

    public string SourceLabel
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(AccountEmail) && !string.IsNullOrWhiteSpace(NodeName))
            {
                return $"{AccountEmail} | {NodeName}";
            }

            if (!string.IsNullOrWhiteSpace(AccountEmail))
            {
                return AccountEmail!;
            }

            if (!string.IsNullOrWhiteSpace(NodeName))
            {
                return NodeName!;
            }

            return "Worker event";
        }
    }

    public string TimestampLabel => CreatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
    public string StatusLabel => string.Equals(Status, "Acknowledged", StringComparison.OrdinalIgnoreCase)
        ? $"Acknowledged {(AcknowledgedAtUtc ?? UpdatedAtUtc ?? CreatedAtUtc).ToLocalTime():yyyy-MM-dd HH:mm:ss}"
        : "Pending";
    public bool HasActionUrl => !string.IsNullOrWhiteSpace(ActionUrl);
    public Visibility ActionButtonVisibility => HasActionUrl ? Visibility.Visible : Visibility.Collapsed;
    public bool CanAcknowledge => !string.Equals(Status, "Acknowledged", StringComparison.OrdinalIgnoreCase);
    public Visibility AcknowledgeButtonVisibility => CanAcknowledge ? Visibility.Visible : Visibility.Collapsed;

    public static WorkerInboxEventViewModel FromContract(WorkerInboxEventResponse response) => new()
    {
        EventId = response.Id,
        EventType = response.EventType,
        Status = response.Status,
        Title = response.Title,
        Message = response.Message,
        ActionUrl = response.ActionUrl,
        AccountId = response.AccountId,
        AccountEmail = response.AccountEmail,
        NodeId = response.NodeId,
        NodeName = response.NodeName,
        CreatedAtUtc = response.CreatedAtUtc,
        UpdatedAtUtc = response.UpdatedAtUtc,
        AcknowledgedAtUtc = response.AcknowledgedAtUtc
    };
}
