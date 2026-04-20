namespace FleetManager.Contracts.Configuration;

public static class FleetManagerDevDefaults
{
    // Neutral development-only defaults. Production deployments should override these explicitly.
    public const string AdminPassword = "FleetManager-DevOnly-ChangeMe!";
    public const string AgentApiKey = "FleetManager-DevOnly-AgentKey";
}
