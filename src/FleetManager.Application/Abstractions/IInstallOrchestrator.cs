namespace FleetManager.Application.Abstractions;

public interface IInstallOrchestrator
{
    Task<Guid> QueueInstallAsync(Guid nodeId, CancellationToken cancellationToken = default);
    Task RetryFailedInstallAsync(Guid installJobId, CancellationToken cancellationToken = default);
}
