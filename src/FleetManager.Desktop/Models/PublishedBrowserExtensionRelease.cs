namespace FleetManager.Desktop.Models;

public sealed record PublishedBrowserExtensionRelease(
    string DisplayName,
    string Version,
    string BundleUrl,
    string BundleSha256Url,
    string BundleSha256,
    string InstallPath);
