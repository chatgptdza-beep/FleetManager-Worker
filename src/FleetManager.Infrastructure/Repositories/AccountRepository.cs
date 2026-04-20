using FleetManager.Application.Abstractions;
using FleetManager.Domain.Entities;
using FleetManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FleetManager.Infrastructure.Repositories;

public sealed class AccountRepository(AppDbContext dbContext) : IAccountRepository
{
    public async Task<IReadOnlyList<Account>> GetAllAsync(Guid? nodeId = null, CancellationToken cancellationToken = default)
    {
        var query = dbContext.Accounts
            .AsNoTracking()
            .Include(x => x.VpsNode)
            .Include(x => x.Alerts)
            .Include(x => x.Proxies)
            .AsQueryable();

        if (nodeId.HasValue)
        {
            query = query.Where(x => x.VpsNodeId == nodeId.Value);
        }

        return await query
            .OrderBy(x => x.Email)
            .ToListAsync(cancellationToken);
    }

    public async Task<Account?> GetByIdAsync(Guid accountId, CancellationToken cancellationToken = default)
        => await dbContext.Accounts
            .AsNoTracking()
            .Include(x => x.VpsNode)
            .Include(x => x.Alerts)
            .Include(x => x.Proxies)
            .Include(x => x.ProxyRotationLogs)
            .Include(x => x.WorkflowStages)
            .FirstOrDefaultAsync(x => x.Id == accountId, cancellationToken);

    public async Task<Account?> GetTrackedByIdAsync(Guid accountId, CancellationToken cancellationToken = default)
        => await dbContext.Accounts
            .Include(x => x.VpsNode)
            .Include(x => x.Alerts)
            .Include(x => x.Proxies)
            .Include(x => x.ProxyRotationLogs)
            .Include(x => x.WorkflowStages)
            .FirstOrDefaultAsync(x => x.Id == accountId, cancellationToken);

    public async Task AddAsync(Account account, CancellationToken cancellationToken = default)
        => await dbContext.Accounts.AddAsync(account, cancellationToken);

    public void Remove(Account account)
        => dbContext.Accounts.Remove(account);

    public async Task DeleteGraphAsync(Account account, CancellationToken cancellationToken = default)
    {
        var accountIdText = account.Id.ToString();

        var relatedWorkerEvents = await dbContext.WorkerInboxEvents
            .Where(workerEvent => workerEvent.AccountId == account.Id)
            .ToListAsync(cancellationToken);
        if (relatedWorkerEvents.Count > 0)
        {
            dbContext.WorkerInboxEvents.RemoveRange(relatedWorkerEvents);
        }

        var relatedCommands = await dbContext.NodeCommands
            .Where(command => command.PayloadJson.Contains(accountIdText))
            .ToListAsync(cancellationToken);
        if (relatedCommands.Count > 0)
        {
            dbContext.NodeCommands.RemoveRange(relatedCommands);
        }

        dbContext.Accounts.Remove(account);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => dbContext.SaveChangesAsync(cancellationToken);
}
