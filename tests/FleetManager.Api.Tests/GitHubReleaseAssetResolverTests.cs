using FleetManager.Contracts.Configuration;
using Xunit;

namespace FleetManager.Api.Tests;

public sealed class GitHubReleaseAssetResolverTests
{
    [Fact]
    public void TryBuildReleaseAssetUrl_builds_url_for_https_repository()
    {
        var built = GitHubReleaseAssetResolver.TryBuildReleaseAssetUrl(
            "https://github.com/chatgptdza-beep/FleetManager-Worker.git",
            FleetManagerReleaseDefaults.AgentBundleReleaseTag,
            FleetManagerReleaseDefaults.AgentBundleFileName,
            out var assetUrl);

        Assert.True(built);
        Assert.Equal(
            "https://github.com/chatgptdza-beep/FleetManager-Worker/releases/download/agent-bundle-latest/fleetmanager-agent-bundle-linux-x64.zip",
            assetUrl);
    }

    [Fact]
    public void TryBuildReleaseAssetUrl_builds_url_for_api_release_assets()
    {
        var built = GitHubReleaseAssetResolver.TryBuildReleaseAssetUrl(
            "https://github.com/chatgptdza-beep/FleetManager-Worker.git",
            FleetManagerReleaseDefaults.ApiBundleReleaseTag,
            FleetManagerReleaseDefaults.ApiBundleFileName,
            out var assetUrl);

        Assert.True(built);
        Assert.Equal(
            "https://github.com/chatgptdza-beep/FleetManager-Worker/releases/download/api-bundle-latest/fleetmanager-api-bundle-linux-x64.zip",
            assetUrl);
    }

    [Fact]
    public void TryBuildReleaseAssetUrl_builds_url_for_browser_extension_release_assets()
    {
        var built = GitHubReleaseAssetResolver.TryBuildReleaseAssetUrl(
            "https://github.com/chatgptdza-beep/FleetManager-Worker.git",
            FleetManagerReleaseDefaults.BrowserExtensionBundleReleaseTag,
            FleetManagerReleaseDefaults.BrowserExtensionBundleFileName,
            out var assetUrl);

        Assert.True(built);
        Assert.Equal(
            "https://github.com/chatgptdza-beep/FleetManager-Worker/releases/download/browser-extension-latest/fleetmanager-browser-extension-bundle.zip",
            assetUrl);
    }

    [Fact]
    public void TryBuildReleaseAssetUrl_builds_url_for_ssh_repository_format()
    {
        var built = GitHubReleaseAssetResolver.TryBuildReleaseAssetUrl(
            "git@github.com:chatgptdza-beep/FleetManager-Worker.git",
            FleetManagerReleaseDefaults.AgentBundleReleaseTag,
            FleetManagerReleaseDefaults.AgentBundleSha256FileName,
            out var assetUrl);

        Assert.True(built);
        Assert.Equal(
            "https://github.com/chatgptdza-beep/FleetManager-Worker/releases/download/agent-bundle-latest/fleetmanager-agent-bundle-linux-x64.zip.sha256",
            assetUrl);
    }

    [Fact]
    public void TryBuildReleaseAssetUrl_rejects_non_github_repository()
    {
        var built = GitHubReleaseAssetResolver.TryBuildReleaseAssetUrl(
            "https://example.com/chatgptdza-beep/FleetManager-Worker.git",
            FleetManagerReleaseDefaults.AgentBundleReleaseTag,
            FleetManagerReleaseDefaults.AgentBundleFileName,
            out var assetUrl);

        Assert.False(built);
        Assert.Equal(string.Empty, assetUrl);
    }
}
