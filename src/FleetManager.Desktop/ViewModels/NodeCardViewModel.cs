using System.Globalization;
using System.Windows;
using System.Windows.Media;
using FleetManager.Contracts.Nodes;
using FleetManager.Desktop.Styling;

namespace FleetManager.Desktop.ViewModels;

public sealed class NodeCardViewModel
{
    public Guid NodeId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string IpAddress { get; init; } = string.Empty;
    public int SshPort { get; init; }
    public string SshUsername { get; init; } = string.Empty;
    public string AuthType { get; init; } = string.Empty;
    public string OsType { get; init; } = string.Empty;
    public string? Region { get; init; }
    public DateTime? LastHeartbeatAtUtc { get; init; }
    public double CpuPercent { get; init; }
    public double RamPercent { get; init; }
    public double DiskPercent { get; init; }
    public double RamUsedGb { get; init; }
    public double StorageUsedGb { get; init; }
    public int PingMs { get; init; }
    public int ActiveSessions { get; init; }
    public int ControlPort { get; init; }
    public string ConnectionState { get; init; } = string.Empty;
    public int ConnectionTimeoutSeconds { get; init; }
    public int AssignedAccountCount { get; init; }
    public int RunningAccounts { get; init; }
    public int ManualAccounts { get; init; }
    public int AlertAccounts { get; init; }
    public string? AgentVersion { get; init; }

    public string NodeDisplayName => $"{Name} - {IpAddress}";
    public bool IsConnected => string.Equals(ConnectionState, "Connected", StringComparison.OrdinalIgnoreCase);
    public bool IsPending => string.Equals(Status, "Pending", StringComparison.OrdinalIgnoreCase);
    public Visibility InstallBarVisibility => IsPending ? Visibility.Visible : Visibility.Collapsed;
    public string InstallStatusLabel => IsPending ? "Installing agent..." : string.Empty;
    public string HealthChipLabel => Status switch
    {
        "Pending" => "Installing",
        "Offline" => "Alert",
        "Degraded" => "Watch",
        _ when string.Equals(ConnectionState, "Degraded", StringComparison.OrdinalIgnoreCase) => "Alert",
        _ when string.Equals(ConnectionState, "Reconnecting", StringComparison.OrdinalIgnoreCase) => "Watch",
        _ => "Stable"
    };

    public Brush HealthChipBackground => HealthChipLabel switch
    {
        "Stable" => UiPalette.SuccessBackground,
        "Watch" or "Installing" => UiPalette.WarningBackground,
        _ => UiPalette.CriticalBackground
    };

    public Brush HealthChipForeground => HealthChipLabel switch
    {
        "Stable" => UiPalette.SuccessForeground,
        "Watch" or "Installing" => UiPalette.WarningForeground,
        _ => UiPalette.CriticalForeground
    };

    public Brush ConnectionBadgeBackground => string.Equals(ConnectionState, "Connected", StringComparison.OrdinalIgnoreCase)
        ? UiPalette.SuccessBackground
        : UiPalette.WarningBackground;

    public Brush ConnectionBadgeForeground => string.Equals(ConnectionState, "Connected", StringComparison.OrdinalIgnoreCase)
        ? UiPalette.SuccessForeground
        : UiPalette.WarningForeground;

    public string BrowsersLabel => Math.Max(AssignedAccountCount, ActiveSessions).ToString(CultureInfo.InvariantCulture);
    public string CpuLabel => $"{CpuPercent:0}%";
    public string RamLabel => $"{RamUsedGb:0} GB";
    public string StorageLabel => $"{StorageUsedGb:0} GB";
    public string PingLabel => PingMs.ToString(CultureInfo.InvariantCulture);
    public string PortLabel => ControlPort.ToString(CultureInfo.InvariantCulture);
    public string TimeoutLabel => $"{ConnectionTimeoutSeconds}s";
    public string ConnectionLabel => $"WS {ConnectionState}";
    public string FooterSummary => $"{RunningAccounts} running | {ManualAccounts} manual | {AlertAccounts} alerts";
    public string HeartbeatLabel => LastHeartbeatAtUtc.HasValue
        ? $"Heartbeat {LastHeartbeatAtUtc.Value.ToLocalTime():HH:mm:ss}"
        : "No heartbeat";
    public string SshLabel => $"{SshUsername}@{IpAddress}:{SshPort}";
    public string RegionLabel => string.IsNullOrWhiteSpace(Region) ? "No region" : Region;
    public string AgentVersionLabel => string.IsNullOrWhiteSpace(AgentVersion) ? "Not reported" : AgentVersion;

    public static NodeCardViewModel FromContract(NodeSummaryResponse response) => new()
    {
        NodeId = response.Id,
        Name = response.Name,
        Status = response.Status,
        IpAddress = response.IpAddress,
        SshPort = response.SshPort,
        SshUsername = response.SshUsername,
        AuthType = response.AuthType,
        OsType = response.OsType,
        Region = response.Region,
        LastHeartbeatAtUtc = response.LastHeartbeatAtUtc,
        CpuPercent = response.CpuPercent,
        RamPercent = response.RamPercent,
        DiskPercent = response.DiskPercent,
        RamUsedGb = response.RamUsedGb,
        StorageUsedGb = response.StorageUsedGb,
        PingMs = response.PingMs,
        ActiveSessions = response.ActiveSessions,
        ControlPort = response.ControlPort,
        ConnectionState = response.ConnectionState,
        ConnectionTimeoutSeconds = response.ConnectionTimeoutSeconds,
        AssignedAccountCount = response.AssignedAccountCount,
        RunningAccounts = response.RunningAccounts,
        ManualAccounts = response.ManualAccounts,
        AlertAccounts = response.AlertAccounts,
        AgentVersion = response.AgentVersion
    };
}
