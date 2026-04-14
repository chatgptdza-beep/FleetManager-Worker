namespace FleetManager.Desktop.Services;

public interface IDesktopSelfHealingService : IAsyncDisposable
{
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}
