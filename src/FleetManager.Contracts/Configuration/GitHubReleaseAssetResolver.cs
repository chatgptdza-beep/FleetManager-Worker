namespace FleetManager.Contracts.Configuration;

public static class FleetManagerReleaseDefaults
{
    public const string RepositoryUrl = "https://github.com/chatgptdza-beep/FleetManager-Worker.git";
    public const string AgentBundleReleaseTag = "agent-bundle-latest";
    public const string AgentBundleFileName = "fleetmanager-agent-bundle-linux-x64.zip";
    public const string AgentBundleSha256FileName = AgentBundleFileName + ".sha256";
    public const string ApiBundleReleaseTag = "api-bundle-latest";
    public const string ApiBundleFileName = "fleetmanager-api-bundle-linux-x64.zip";
    public const string ApiBundleSha256FileName = ApiBundleFileName + ".sha256";
    public const string BrowserExtensionBundleReleaseTag = "browser-extension-latest";
    public const string BrowserExtensionBundleFileName = "fleetmanager-browser-extension-bundle.zip";
    public const string BrowserExtensionBundleSha256FileName = BrowserExtensionBundleFileName + ".sha256";
}

public static class GitHubReleaseAssetResolver
{
    public static bool TryBuildReleaseAssetUrl(string repoUrl, string releaseTag, string assetFileName, out string assetUrl)
    {
        assetUrl = string.Empty;
        if (!TryExtractRepositoryCoordinates(repoUrl, out var owner, out var repo))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(releaseTag) || string.IsNullOrWhiteSpace(assetFileName))
        {
            return false;
        }

        assetUrl =
            $"https://github.com/{owner}/{repo}/releases/download/{Uri.EscapeDataString(releaseTag.Trim('/'))}/{Uri.EscapeDataString(assetFileName.Trim('/'))}";
        return true;
    }

    public static bool TryExtractRepositoryCoordinates(string repoUrl, out string owner, out string repo)
    {
        owner = string.Empty;
        repo = string.Empty;

        if (string.IsNullOrWhiteSpace(repoUrl))
        {
            return false;
        }

        string? repoPath = null;
        var normalized = repoUrl.Trim();

        if (Uri.TryCreate(normalized, UriKind.Absolute, out var repoUri))
        {
            if (!string.Equals(repoUri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            repoPath = repoUri.AbsolutePath;
        }
        else if (normalized.StartsWith("git@github.com:", StringComparison.OrdinalIgnoreCase))
        {
            repoPath = normalized["git@github.com:".Length..];
        }

        if (string.IsNullOrWhiteSpace(repoPath))
        {
            return false;
        }

        var segments = repoPath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length < 2)
        {
            return false;
        }

        owner = segments[0];
        repo = segments[1];

        if (repo.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            repo = repo[..^4];
        }

        return !string.IsNullOrWhiteSpace(owner) && !string.IsNullOrWhiteSpace(repo);
    }
}
