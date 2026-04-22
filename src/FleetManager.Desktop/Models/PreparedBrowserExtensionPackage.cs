namespace FleetManager.Desktop.Models;

public sealed record PreparedBrowserExtensionPackage(
    string DisplayName,
    string Version,
    string SourcePath,
    string BundleBase64,
    string BundleSha256,
    string InstallPath);
