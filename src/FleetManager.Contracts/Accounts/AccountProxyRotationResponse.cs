namespace FleetManager.Contracts.Accounts;

public sealed class AccountProxyRotationResponse
{
    public int FromOrder { get; set; }
    public int ToOrder { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime RotatedAtUtc { get; set; }
}
