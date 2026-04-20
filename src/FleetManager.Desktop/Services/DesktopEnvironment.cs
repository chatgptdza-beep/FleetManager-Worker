using FleetManager.Contracts.Configuration;

namespace FleetManager.Desktop.Services;

internal static class DesktopEnvironment
{
    public static string ResolveOperatorPassword()
        => ResolveFirstNonEmpty(
            FleetManagerDevDefaults.AdminPassword,
            "FLEETMANAGER_API_PASSWORD",
            "AdminPassword");

    public static string ResolveAgentApiKey()
        => ResolveFirstNonEmpty(
            FleetManagerDevDefaults.AgentApiKey,
            "FLEETMANAGER_AGENT_API_KEY",
            "AgentApiKey");

    public static bool ShouldPersistSshCredentials()
    {
        var configured = ResolveFirstNonEmpty(defaultValue: null, "FLEETMANAGER_STORE_SSH_CREDENTIALS");
        return bool.TryParse(configured, out var persist) && persist;
    }

    private static string ResolveFirstNonEmpty(string? defaultValue, params string[] environmentVariableNames)
    {
        foreach (var environmentVariableName in environmentVariableNames)
        {
            var configured = Environment.GetEnvironmentVariable(environmentVariableName);
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return configured.Trim();
            }
        }

        return defaultValue ?? string.Empty;
    }
}
