using FleetManager.Desktop.Models;

namespace FleetManager.Desktop.Services;

public interface IBrowserExtensionReleaseService
{
    Task<PublishedBrowserExtensionRelease> PublishAsync(
        string sourcePath,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);

    Task<PublishedBrowserExtensionRelease?> TryGetPublishedReleaseAsync(CancellationToken cancellationToken = default);
}
