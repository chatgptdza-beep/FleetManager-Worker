using FleetManager.Application.Abstractions;
using FleetManager.Contracts.Accounts;
using FleetManager.Domain.Entities;
using FleetManager.Domain.Enums;

namespace FleetManager.Application.Services;

public sealed class AccountService(IAccountRepository accountRepository, INodeRepository nodeRepository) : IAccountService
{
    public async Task<IReadOnlyList<AccountSummaryResponse>> GetAccountsAsync(Guid? nodeId = null, CancellationToken cancellationToken = default)
    {
        var accounts = await accountRepository.GetAllAsync(nodeId, cancellationToken);
        return accounts.Select(MapSummary).ToList();
    }

    public async Task<AccountSummaryResponse?> GetAccountAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        var account = await accountRepository.GetByIdAsync(accountId, cancellationToken);
        return account is null ? null : MapSummary(account);
    }

    public async Task<AccountStageAlertDetailsResponse?> GetAccountStageAlertsAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        var account = await accountRepository.GetByIdAsync(accountId, cancellationToken);
        return account is null ? null : MapDetails(account);
    }

    public async Task<AccountSummaryResponse> CreateAccountAsync(CreateAccountRequest request, CancellationToken cancellationToken = default)
    {
        var node = await nodeRepository.GetByIdAsync(request.NodeId, cancellationToken)
            ?? throw new InvalidOperationException("Node not found.");

        var status = ParseStatus(request.Status);
        var account = new Account
        {
            Email = NormalizeRequired(request.Email, nameof(request.Email)),
            Username = NormalizeRequired(request.Username, nameof(request.Username)),
            Status = status,
            VpsNodeId = node.Id,
            CurrentStageCode = "ready",
            CurrentStageName = "Ready",
            LastStageTransitionAtUtc = DateTime.UtcNow
        };

        await accountRepository.AddAsync(account, cancellationToken);
        await accountRepository.SaveChangesAsync(cancellationToken);

        var created = await accountRepository.GetByIdAsync(account.Id, cancellationToken)
            ?? throw new InvalidOperationException("Account creation failed.");

        return MapSummary(created);
    }

    public async Task<AccountSummaryResponse?> UpdateAccountAsync(Guid accountId, UpdateAccountRequest request, CancellationToken cancellationToken = default)
    {
        var account = await accountRepository.GetTrackedByIdAsync(accountId, cancellationToken);
        if (account is null)
        {
            return null;
        }

        account.Email = NormalizeRequired(request.Email, nameof(request.Email));
        account.Username = NormalizeRequired(request.Username, nameof(request.Username));
        account.Status = ParseStatus(request.Status);
        account.UpdatedAtUtc = DateTime.UtcNow;

        await accountRepository.SaveChangesAsync(cancellationToken);

        var updated = await accountRepository.GetByIdAsync(accountId, cancellationToken)
            ?? throw new InvalidOperationException("Updated account not found.");

        return MapSummary(updated);
    }

    public async Task<bool> DeleteAccountAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        var account = await accountRepository.GetTrackedByIdAsync(accountId, cancellationToken);
        if (account is null)
        {
            return false;
        }

        accountRepository.Remove(account);
        await accountRepository.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<AccountSummaryResponse?> SetAccountStatusManualAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        var account = await accountRepository.GetTrackedByIdAsync(accountId, cancellationToken);
        if (account is null) return null;

        account.Status = AccountStatus.Manual;
        account.UpdatedAtUtc = DateTime.UtcNow;
        await accountRepository.SaveChangesAsync(cancellationToken);

        var updated = await accountRepository.GetByIdAsync(accountId, cancellationToken);
        return updated is null ? null : MapSummary(updated);
    }

    private static AccountSummaryResponse MapSummary(Account account)
    {
        var activeAlert = GetActiveAlert(account);
        return new AccountSummaryResponse
        {
            Id = account.Id,
            Email = account.Email,
            Username = account.Username,
            Status = account.Status.ToString(),
            NodeId = account.VpsNodeId,
            NodeName = account.VpsNode?.Name ?? string.Empty,
            NodeIpAddress = account.VpsNode?.IpAddress ?? string.Empty,
            CurrentStage = account.CurrentStageName,
            ActiveAlertSeverity = activeAlert?.Severity.ToString(),
            ActiveAlertStage = activeAlert?.StageName,
            ActiveAlertTitle = activeAlert?.Title,
            ActiveAlertMessage = activeAlert?.Message,
            CurrentProxyIndex = account.CurrentProxyIndex,
            ProxyCount = account.Proxies.Count
        };
    }

    private static AccountStageAlertDetailsResponse MapDetails(Account account)
    {
        var activeAlert = GetActiveAlert(account);
        return new AccountStageAlertDetailsResponse
        {
            AccountId = account.Id,
            Email = account.Email,
            Username = account.Username,
            Status = account.Status.ToString(),
            NodeId = account.VpsNodeId,
            NodeName = account.VpsNode?.Name ?? string.Empty,
            NodeIpAddress = account.VpsNode?.IpAddress ?? string.Empty,
            CurrentStage = account.CurrentStageName,
            ActiveAlertSeverity = activeAlert?.Severity.ToString(),
            ActiveAlertStage = activeAlert?.StageName,
            ActiveAlertTitle = activeAlert?.Title,
            ActiveAlertMessage = activeAlert?.Message,
            CurrentProxyIndex = account.CurrentProxyIndex,
            ProxyCount = account.Proxies.Count,
            ProxyRotations = account.ProxyRotationLogs
                .OrderByDescending(log => log.RotatedAtUtc)
                .Take(8)
                .Select(log => new AccountProxyRotationResponse
                {
                    FromOrder = log.FromOrder,
                    ToOrder = log.ToOrder,
                    Reason = log.Reason,
                    RotatedAtUtc = log.RotatedAtUtc
                })
                .ToList(),
            Stages = account.WorkflowStages
                .OrderBy(x => x.DisplayOrder)
                .Select(stage => new AccountWorkflowStageResponse
                {
                    StageCode = stage.StageCode,
                    StageName = stage.StageName,
                    State = stage.State.ToString(),
                    Message = stage.Message,
                    OccurredAtUtc = stage.OccurredAtUtc
                })
                .ToList()
        };
    }

    private static AccountAlert? GetActiveAlert(Account account)
        => account.Alerts
            .Where(alert => alert.IsActive)
            .OrderByDescending(alert => alert.CreatedAtUtc)
            .FirstOrDefault();

    private static AccountStatus ParseStatus(string status)
    {
        if (!Enum.TryParse<AccountStatus>(status, true, out var parsed))
        {
            throw new InvalidOperationException("Unsupported account status.");
        }

        return parsed;
    }

    private static string NormalizeRequired(string value, string fieldName)
    {
        var trimmed = value.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw new InvalidOperationException($"{fieldName} is required.");
        }

        return trimmed;
    }
}
