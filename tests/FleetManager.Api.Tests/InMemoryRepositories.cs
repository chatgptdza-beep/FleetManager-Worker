using FleetManager.Application.Abstractions;
using FleetManager.Domain.Entities;

namespace FleetManager.Api.Tests;

internal sealed class InMemoryNodeRepository : INodeRepository
{
    public List<VpsNode> Nodes { get; } = new();

    public Task<IReadOnlyList<VpsNode>> GetAllAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<VpsNode>>(Nodes.ToList());

    public Task<VpsNode?> GetByIdAsync(Guid nodeId, CancellationToken cancellationToken = default)
        => Task.FromResult(Nodes.FirstOrDefault(node => node.Id == nodeId));

    public Task<VpsNode?> GetByIdReadOnlyAsync(Guid nodeId, CancellationToken cancellationToken = default)
        => Task.FromResult(Nodes.FirstOrDefault(node => node.Id == nodeId));

    public Task<NodeCommand?> GetCommandByIdAsync(Guid commandId, CancellationToken cancellationToken = default)
        => Task.FromResult(Nodes.SelectMany(node => node.Commands).FirstOrDefault(command => command.Id == commandId));

    public Task<NodeCommand?> ClaimNextPendingCommandAsync(Guid nodeId, DateTime dispatchedAtUtc, DateTime redispatchCutoffUtc, CancellationToken cancellationToken = default)
    {
        var command = Nodes
            .FirstOrDefault(node => node.Id == nodeId)?
            .Commands
            .Where(command => command.Status == FleetManager.Domain.Enums.CommandStatus.Pending
                || (command.Status == FleetManager.Domain.Enums.CommandStatus.Dispatched
                    && command.UpdatedAtUtc.HasValue
                    && command.UpdatedAtUtc.Value <= redispatchCutoffUtc))
            .OrderBy(command => command.CreatedAtUtc)
            .FirstOrDefault();

        if (command is not null)
        {
            command.Status = FleetManager.Domain.Enums.CommandStatus.Dispatched;
            command.UpdatedAtUtc = dispatchedAtUtc;
        }

        return Task.FromResult(command);
    }

    public Task AddAsync(VpsNode node, CancellationToken cancellationToken = default)
    {
        Nodes.Add(node);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(VpsNode node, CancellationToken cancellationToken = default)
    {
        Nodes.Remove(node);
        return Task.CompletedTask;
    }

    public Task AddCommandAsync(NodeCommand command, CancellationToken cancellationToken = default)
    {
        var node = Nodes.FirstOrDefault(candidate => candidate.Id == command.VpsNodeId);
        if (node is not null)
        {
            node.Commands.Add(command);
        }

        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

internal sealed class InMemoryAccountRepository : IAccountRepository
{
    private readonly InMemoryNodeRepository? _nodeRepository;

    public InMemoryAccountRepository(InMemoryNodeRepository? nodeRepository = null)
    {
        _nodeRepository = nodeRepository;
    }

    public List<Account> Accounts { get; } = new();

    public Task<IReadOnlyList<Account>> GetAllAsync(Guid? nodeId = null, CancellationToken cancellationToken = default)
    {
        var query = Accounts.AsEnumerable();
        if (nodeId.HasValue)
        {
            query = query.Where(account => account.VpsNodeId == nodeId.Value);
        }

        return Task.FromResult<IReadOnlyList<Account>>(query.ToList());
    }

    public Task<Account?> GetByIdAsync(Guid accountId, CancellationToken cancellationToken = default)
        => Task.FromResult(Accounts.FirstOrDefault(account => account.Id == accountId));

    public Task<Account?> GetTrackedByIdAsync(Guid accountId, CancellationToken cancellationToken = default)
        => Task.FromResult(Accounts.FirstOrDefault(account => account.Id == accountId));

    public Task AddAsync(Account account, CancellationToken cancellationToken = default)
    {
        Accounts.Add(account);

        if (_nodeRepository is not null)
        {
            var node = _nodeRepository.Nodes.FirstOrDefault(candidate => candidate.Id == account.VpsNodeId);
            if (node is not null)
            {
                account.VpsNode = node;
                if (!node.Accounts.Contains(account))
                {
                    node.Accounts.Add(account);
                }
            }
        }

        return Task.CompletedTask;
    }

    public void Remove(Account account)
    {
        Accounts.Remove(account);
        account.VpsNode?.Accounts.Remove(account);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
