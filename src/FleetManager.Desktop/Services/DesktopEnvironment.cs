using FleetManager.Contracts.Configuration;

namespace FleetManager.Desktop.Services;

internal static class DesktopEnvironment
{
    public static string ResolveOperatorPassword()
        => ResolveFirstNonEmpty(
            FleetManagerDevDefaults.AdminPassword,
            "FLEETMANAGER_API_PASSWORD",
            "AdminPassword") ?? string.Empty;

    public static string ResolveAgentApiKey()
        => ResolveFirstNonEmpty(
            FleetManagerDevDefaults.AgentApiKey,
            "FLEETMANAGER_AGENT_API_KEY",
            "AgentApiKey") ?? string.Empty;

    public static string ResolveRepositoryUrl()
        => ResolveFirstNonEmpty(
            FleetManagerReleaseDefaults.RepositoryUrl,
            "FLEETMANAGER_REPO_URL") ?? string.Empty;

    public static string ResolveAgentBundleReleaseTag()
        => ResolveFirstNonEmpty(
            FleetManagerReleaseDefaults.AgentBundleReleaseTag,
            "FLEETMANAGER_AGENT_BUNDLE_RELEASE_TAG") ?? string.Empty;

    public static string ResolveAgentBundleUrl()
    {
        var explicitUrl = ResolveFirstNonEmpty(defaultValue: null, "FLEETMANAGER_AGENT_BUNDLE_URL");
        if (!string.IsNullOrWhiteSpace(explicitUrl))
        {
            return explicitUrl.Trim();
        }

        return GitHubReleaseAssetResolver.TryBuildReleaseAssetUrl(
            ResolveRepositoryUrl(),
            ResolveAgentBundleReleaseTag(),
            FleetManagerReleaseDefaults.AgentBundleFileName,
            out var bundleUrl)
            ? bundleUrl
            : string.Empty;
    }

    public static string ResolveAgentBundleSha256Url()
    {
        var explicitUrl = ResolveFirstNonEmpty(defaultValue: null, "FLEETMANAGER_AGENT_BUNDLE_SHA256_URL");
        if (!string.IsNullOrWhiteSpace(explicitUrl))
        {
            return explicitUrl.Trim();
        }

        return GitHubReleaseAssetResolver.TryBuildReleaseAssetUrl(
            ResolveRepositoryUrl(),
            ResolveAgentBundleReleaseTag(),
            FleetManagerReleaseDefaults.AgentBundleSha256FileName,
            out var bundleUrl)
            ? bundleUrl
            : string.Empty;
    }

    public static string? ResolveAgentBundlePathOverride()
        => ResolveFirstNonEmpty(defaultValue: null, "FLEETMANAGER_AGENT_BUNDLE_PATH");

    public static string? ResolveAgentBundleSha256()
        => ResolveFirstNonEmpty(defaultValue: null, "FLEETMANAGER_AGENT_BUNDLE_SHA256");

    public static bool ShouldPersistSshCredentials()
    {
        var configured = ResolveFirstNonEmpty(defaultValue: null, "FLEETMANAGER_STORE_SSH_CREDENTIALS");
        return bool.TryParse(configured, out var persist) && persist;
    }

    private static string? ResolveFirstNonEmpty(string? defaultValue, params string[] environmentVariableNames)
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
