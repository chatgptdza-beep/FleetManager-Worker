using FleetManager.Application.Abstractions;
using FleetManager.Domain.Entities;
using FleetManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FleetManager.Infrastructure.Repositories;

public sealed class NodeRepository(AppDbContext dbContext) : INodeRepository
{
    public async Task<IReadOnlyList<VpsNode>> GetAllAsync(CancellationToken cancellationToken = default)
        => await dbContext.VpsNodes
            .AsNoTracking()
            .Include(x => x.Accounts)
            .ThenInclude(x => x.Alerts)
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

    public async Task<VpsNode?> GetByIdAsync(Guid nodeId, CancellationToken cancellationToken = default)
        => await dbContext.VpsNodes
            .Include(x => x.Commands)
            .Include(x => x.Accounts)
            .ThenInclude(x => x.Alerts)
            .FirstOrDefaultAsync(x => x.Id == nodeId, cancellationToken);

    public async Task<NodeCommand?> GetCommandByIdAsync(Guid commandId, CancellationToken cancellationToken = default)
        => await dbContext.NodeCommands
            .Include(x => x.VpsNode)
            .FirstOrDefaultAsync(x => x.Id == commandId, cancellationToken);

    public async Task AddAsync(VpsNode node, CancellationToken cancellationToken = default)
        => await dbContext.VpsNodes.AddAsync(node, cancellationToken);

    public Task DeleteAsync(VpsNode node, CancellationToken cancellationToken = default)
    {
        dbContext.VpsNodes.Remove(node);
        return Task.CompletedTask;
    }

    public async Task AddCommandAsync(NodeCommand command, CancellationToken cancellationToken = default)
        => await dbContext.NodeCommands.AddAsync(command, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => dbContext.SaveChangesAsync(cancellationToken);
}
