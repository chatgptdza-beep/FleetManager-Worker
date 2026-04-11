using FleetManager.Application.Services;
using FleetManager.Contracts.Accounts;
using FleetManager.Domain.Entities;
using FleetManager.Domain.Enums;
using Xunit;

namespace FleetManager.Api.Tests;

public sealed class AccountServiceTests
{
    [Fact]
    public async Task GetAccountsAsync_filters_by_node_id()
    {
        var nodeRepository = new InMemoryNodeRepository();
        var paris = new VpsNode { Name = "Paris", IpAddress = "10.0.0.21" };
        var lagos = new VpsNode { Name = "Lagos", IpAddress = "10.0.0.68" };
        nodeRepository.Nodes.AddRange(new[] { paris, lagos });

        var accountRepository = new InMemoryAccountRepository(nodeRepository);
        accountRepository.Accounts.AddRange(new[]
        {
            CreateAccount("alpha@example.com", "alpha", paris, AccountStatus.Running),
            CreateAccount("delta@example.com", "delta", lagos, AccountStatus.Manual)
        });

        var service = new AccountService(accountRepository, nodeRepository);
        var accounts = await service.GetAccountsAsync(paris.Id);

        Assert.Single(accounts);
        Assert.Equal(paris.Id, accounts[0].NodeId);
        Assert.Equal("alpha@example.com", accounts[0].Email);
    }

    [Fact]
    public async Task CreateAccountAsync_assigns_selected_node()
    {
        var nodeRepository = new InMemoryNodeRepository();
        var node = new VpsNode { Name = "Paris", IpAddress = "10.0.0.21" };
        nodeRepository.Nodes.Add(node);
        var accountRepository = new InMemoryAccountRepository(nodeRepository);
        var service = new AccountService(accountRepository, nodeRepository);

        var created = await service.CreateAccountAsync(new CreateAccountRequest
        {
            NodeId = node.Id,
            Email = "new@example.com",
            Username = "new.user",
            Status = "Running"
        });

        Assert.Equal(node.Id, created.NodeId);
        Assert.Equal("new.user", created.Username);
        Assert.Single(accountRepository.Accounts);
        Assert.Equal(node.Id, accountRepository.Accounts[0].VpsNodeId);
        Assert.Single(node.Accounts);
    }

    [Fact]
    public async Task UpdateAccountAsync_keeps_existing_node_id()
    {
        var nodeRepository = new InMemoryNodeRepository();
        var node = new VpsNode { Name = "Paris", IpAddress = "10.0.0.21" };
        nodeRepository.Nodes.Add(node);
        var accountRepository = new InMemoryAccountRepository(nodeRepository);
        var account = CreateAccount("alpha@example.com", "alpha", node, AccountStatus.Running);
        accountRepository.Accounts.Add(account);
        var service = new AccountService(accountRepository, nodeRepository);

        var updated = await service.UpdateAccountAsync(account.Id, new UpdateAccountRequest
        {
            Email = "updated@example.com",
            Username = "updated.user",
            Status = "Paused"
        });

        Assert.NotNull(updated);
        Assert.Equal(node.Id, updated!.NodeId);
        Assert.Equal(node.Id, account.VpsNodeId);
        Assert.Equal("updated@example.com", account.Email);
        Assert.Equal(AccountStatus.Paused, account.Status);
    }

    [Fact]
    public async Task DeleteAccountAsync_removes_account()
    {
        var nodeRepository = new InMemoryNodeRepository();
        var node = new VpsNode { Name = "Paris", IpAddress = "10.0.0.21" };
        nodeRepository.Nodes.Add(node);
        var accountRepository = new InMemoryAccountRepository(nodeRepository);
        var account = CreateAccount("alpha@example.com", "alpha", node, AccountStatus.Running);
        accountRepository.Accounts.Add(account);
        var service = new AccountService(accountRepository, nodeRepository);

        var deleted = await service.DeleteAccountAsync(account.Id);

        Assert.True(deleted);
        Assert.Empty(accountRepository.Accounts);
        Assert.Empty(node.Accounts);
    }

    private static Account CreateAccount(string email, string username, VpsNode node, AccountStatus status)
    {
        var account = new Account
        {
            Email = email,
            Username = username,
            Status = status,
            VpsNodeId = node.Id,
            VpsNode = node,
            CurrentStageCode = "slot_search",
            CurrentStageName = "Slot Search"
        };

        node.Accounts.Add(account);
        return account;
    }
}
