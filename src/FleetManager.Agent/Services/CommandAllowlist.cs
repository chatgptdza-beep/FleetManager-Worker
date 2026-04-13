namespace FleetManager.Agent.Services;

/// <summary>
/// Delegates to the shared <see cref="FleetManager.Contracts.CommandAllowlist"/>
/// so the allowed-command set is defined in one place.
/// </summary>
public static class CommandAllowlist
{
    public static bool IsAllowed(string commandName)
        => FleetManager.Contracts.CommandAllowlist.IsAllowed(commandName);
}
