using FleetManager.Contracts.Accounts;

namespace FleetManager.Desktop.ViewModels;

public sealed class AccountProxyRotationViewModel
{
    public string SlotSummary { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public string TimestampLabel { get; init; } = string.Empty;

    public static AccountProxyRotationViewModel FromContract(AccountProxyRotationResponse response) => new()
    {
        SlotSummary = $"{response.FromOrder + 1} -> {response.ToOrder + 1}",
        Reason = string.IsNullOrWhiteSpace(response.Reason) ? "No reason recorded" : response.Reason,
        TimestampLabel = response.RotatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
    };
}
