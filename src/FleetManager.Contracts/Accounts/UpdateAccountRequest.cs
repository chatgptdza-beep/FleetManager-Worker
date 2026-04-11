namespace FleetManager.Contracts.Accounts;

public sealed class UpdateAccountRequest
{
    public string Email { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Status { get; set; } = "Running";
}
