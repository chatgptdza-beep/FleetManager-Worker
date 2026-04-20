using FleetManager.Application.Abstractions;
using FleetManager.Domain.Entities;
using FleetManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Data;

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

    public async Task<VpsNode?> GetByIdReadOnlyAsync(Guid nodeId, CancellationToken cancellationToken = default)
        => await dbContext.VpsNodes
            .AsNoTracking()
            .Include(x => x.Accounts)
            .ThenInclude(x => x.Alerts)
            .FirstOrDefaultAsync(x => x.Id == nodeId, cancellationToken);

    public async Task<VpsNode?> GetByIpAddressAsync(string ipAddress, CancellationToken cancellationToken = default)
        => await dbContext.VpsNodes
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.IpAddress == ipAddress, cancellationToken);

    public async Task<NodeCommand?> GetCommandByIdAsync(Guid commandId, CancellationToken cancellationToken = default)
        => await dbContext.NodeCommands
            .Include(x => x.VpsNode)
            .FirstOrDefaultAsync(x => x.Id == commandId, cancellationToken);

    public async Task<NodeCommand?> ClaimNextPendingCommandAsync(
        Guid nodeId,
        DateTime dispatchedAtUtc,
        DateTime redispatchCutoffUtc,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        var command = await dbContext.NodeCommands
            .FromSqlInterpolated($@"
SELECT *
FROM ""NodeCommands""
WHERE ""VpsNodeId"" = {nodeId}
  AND (
    ""Status"" = {FleetManager.Domain.Enums.CommandStatus.Pending.ToString()}
    OR (
      ""Status"" = {FleetManager.Domain.Enums.CommandStatus.Dispatched.ToString()}
      AND ""UpdatedAtUtc"" IS NOT NULL
      AND ""UpdatedAtUtc"" <= {redispatchCutoffUtc}
    )
  )
ORDER BY ""CreatedAtUtc""
LIMIT 1
FOR UPDATE SKIP LOCKED")
            .AsTracking()
            .SingleOrDefaultAsync(cancellationToken);

        if (command is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        command.Status = FleetManager.Domain.Enums.CommandStatus.Dispatched;
        command.UpdatedAtUtc = dispatchedAtUtc;

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return command;
    }

    public async Task AddAsync(VpsNode node, CancellationToken cancellationToken = default)
        => await dbContext.VpsNodes.AddAsync(node, cancellationToken);

    public Task DeleteAsync(VpsNode node, CancellationToken cancellationToken = default)
    {
        dbContext.VpsNodes.Remove(node);
        return Task.CompletedTask;
    }

    public async Task DeleteGraphAsync(VpsNode node, CancellationToken cancellationToken = default)
    {
        var accountIds = node.Accounts
            .Select(account => account.Id)
            .ToList();

        var relatedWorkerEvents = await dbContext.WorkerInboxEvents
            .Where(workerEvent =>
                workerEvent.VpsNodeId == node.Id
                || (workerEvent.AccountId.HasValue && accountIds.Contains(workerEvent.AccountId.Value)))
            .ToListAsync(cancellationToken);
        if (relatedWorkerEvents.Count > 0)
        {
            dbContext.WorkerInboxEvents.RemoveRange(relatedWorkerEvents);
        }

        var relatedCapabilities = await dbContext.NodeCapabilities
            .Where(capability => capability.VpsNodeId == node.Id)
            .ToListAsync(cancellationToken);
        if (relatedCapabilities.Count > 0)
        {
            dbContext.NodeCapabilities.RemoveRange(relatedCapabilities);
        }

        if (node.Accounts.Count > 0)
        {
            dbContext.Accounts.RemoveRange(node.Accounts);
        }

        dbContext.VpsNodes.Remove(node);
    }

    public async Task AddCommandAsync(NodeCommand command, CancellationToken cancellationToken = default)
        => await dbContext.NodeCommands.AddAsync(command, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => dbContext.SaveChangesAsync(cancellationToken);
}
