using FleetManager.Domain.Entities;

namespace FleetManager.Application.Abstractions;

public interface IAccountRepository
{
    Task<IReadOnlyList<Account>> GetAllAsync(Guid? nodeId = null, CancellationToken cancellationToken = default);
    Task<Account?> GetByIdAsync(Guid accountId, CancellationToken cancellationToken = default);
    Task<Account?> GetTrackedByIdAsync(Guid accountId, CancellationToken cancellationToken = default);
    Task AddAsync(Account account, CancellationToken cancellationToken = default);
    void Remove(Account account);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
