using FleetManager.Domain.Entities;

namespace FleetManager.Application.Abstractions;

public interface INodeRepository
{
    Task<IReadOnlyList<VpsNode>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<VpsNode?> GetByIdAsync(Guid nodeId, CancellationToken cancellationToken = default);
    Task<VpsNode?> GetByIdReadOnlyAsync(Guid nodeId, CancellationToken cancellationToken = default);
    Task<NodeCommand?> GetCommandByIdAsync(Guid commandId, CancellationToken cancellationToken = default);
    Task<NodeCommand?> ClaimNextPendingCommandAsync(Guid nodeId, DateTime dispatchedAtUtc, DateTime redispatchCutoffUtc, CancellationToken cancellationToken = default);
    Task AddAsync(VpsNode node, CancellationToken cancellationToken = default);
    Task DeleteAsync(VpsNode node, CancellationToken cancellationToken = default);
    Task AddCommandAsync(NodeCommand command, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
