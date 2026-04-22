using FleetManager.Desktop.Models;

namespace FleetManager.Desktop.Services;

public interface IBrowserExtensionReleaseService
{
    Task<PreparedBrowserExtensionPackage> PrepareAsync(
        string sourcePath,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);

    Task<PreparedBrowserExtensionPackage?> TryPrepareFromSourceAsync(
        string? sourcePath,
        CancellationToken cancellationToken = default);
}
