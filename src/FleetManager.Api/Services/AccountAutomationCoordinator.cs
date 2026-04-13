using System.Data;
using FleetManager.Api.Hubs;
using FleetManager.Application.Abstractions;
using FleetManager.Contracts.Accounts;
using FleetManager.Domain.Entities;
using FleetManager.Domain.Enums;
using FleetManager.Infrastructure.Persistence;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace FleetManager.Api.Services;

public interface IAccountAutomationCoordinator
{
    Task<InjectProxiesResult?> InjectProxiesAsync(Guid accountId, string rawProxies, bool replaceExisting, CancellationToken cancellationToken = default);
    Task<ProxyRotationResult?> RotateProxyAsync(Guid accountId, string? reason, CancellationToken cancellationToken = default);
    Task<AccountSummaryResponse?> MarkManualRequiredAsync(Guid accountId, string? vncUrl, CancellationToken cancellationToken = default);
    Task<bool> RequestManualTakeoverAsync(Guid accountId, string vncUrl, CancellationToken cancellationToken = default);
    Task<AccountSummaryResponse?> CompleteManualTakeoverAsync(Guid accountId, CancellationToken cancellationToken = default);
}

public sealed record InjectProxiesResult(int InjectedCount, int TotalProxies, int ClearedCount);
public sealed record ProxyRotationResult(int NewIndex);

public sealed class AccountAutomationCoordinator(
    AppDbContext dbContext,
    IAccountService accountService,
    IWorkerInboxService workerInboxService,
    IHubContext<OperationsHub, IOperationsClient> hubContext) : IAccountAutomationCoordinator
{
    public async Task<InjectProxiesResult?> InjectProxiesAsync(Guid accountId, string rawProxies, bool replaceExisting, CancellationToken cancellationToken = default)
    {
        var account = await dbContext.Accounts
            .Include(a => a.Proxies)
            .FirstOrDefaultAsync(a => a.Id == accountId, cancellationToken);
        if (account is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(rawProxies))
        {
            throw new InvalidOperationException("Proxy list is empty.");
        }

        var lines = rawProxies.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var injectedCount = 0;
        var validCount = 0;
        var clearedCount = 0;

        if (replaceExisting && account.Proxies.Count > 0)
        {
            clearedCount = account.Proxies.Count;
            dbContext.ProxyEntries.RemoveRange(account.Proxies.ToList());
            account.Proxies.Clear();
            account.CurrentProxyIndex = 0;
        }

        var order = replaceExisting ? 0 : account.Proxies.Count;
        var seenSignatures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!replaceExisting)
        {
            foreach (var proxy in account.Proxies)
            {
                seenSignatures.Add(BuildProxySignature(proxy.Host, proxy.Port, proxy.Username, proxy.Password));
            }
        }

        foreach (var line in lines)
        {
            var entry = ParseProxyLine(accountId, line, order);
            if (entry is null)
            {
                continue;
            }

            validCount++;
            if (!seenSignatures.Add(BuildProxySignature(entry.Host, entry.Port, entry.Username, entry.Password)))
            {
                continue;
            }

            dbContext.ProxyEntries.Add(entry);
            order++;
            injectedCount++;
        }

        if (injectedCount == 0)
        {
            throw new InvalidOperationException(validCount == 0
                ? "No valid proxy lines were found. Use ip:port or ip:port:user:password."
                : "All provided proxies already exist in the target pool.");
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        var totalProxies = await dbContext.ProxyEntries.CountAsync(proxy => proxy.AccountId == accountId, cancellationToken);
        return new InjectProxiesResult(injectedCount, totalProxies, clearedCount);
    }

    public async Task<ProxyRotationResult?> RotateProxyAsync(Guid accountId, string? reason, CancellationToken cancellationToken = default)
    {
        const int maxRetries = 3;
        for (var attempt = 0; attempt < maxRetries; attempt++)
        {
            await using var transaction = dbContext.Database.IsRelational()
                ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
                : null;

            var account = await dbContext.Accounts
                .Include(a => a.Proxies)
                .Include(a => a.Alerts)
                .Include(a => a.WorkflowStages)
                .FirstOrDefaultAsync(a => a.Id == accountId, cancellationToken);
            if (account is null)
            {
                return null;
            }

            if (!account.Proxies.Any())
            {
                throw new InvalidOperationException("No proxies available for rotation.");
            }

            var oldIndex = account.CurrentProxyIndex;
            account.CurrentProxyIndex = (account.CurrentProxyIndex + 1) % account.Proxies.Count;

            dbContext.ProxyRotationLogs.Add(new ProxyRotationLog
            {
                AccountId = accountId,
                FromOrder = oldIndex,
                ToOrder = account.CurrentProxyIndex,
                Reason = string.IsNullOrWhiteSpace(reason) ? "Manual or 429 Error" : reason.Trim(),
                RotatedAtUtc = DateTime.UtcNow
            });
            AppendWorkflowStage(
                account,
                stageCode: "proxy_rotation",
                stageName: "Proxy Rotation",
                state: WorkflowStageState.Warning,
                message: $"Proxy rotated from slot {oldIndex} to {account.CurrentProxyIndex}. Reason: {(string.IsNullOrWhiteSpace(reason) ? "Manual or 429 Error" : reason.Trim())}");

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                if (transaction is not null)
                {
                    await transaction.CommitAsync(cancellationToken);
                }
            }
            catch (DbUpdateConcurrencyException) when (attempt < maxRetries - 1)
            {
                // Another request rotated the proxy concurrently; reload and retry
                foreach (var entry in dbContext.ChangeTracker.Entries())
                {
                    await entry.ReloadAsync(cancellationToken);
                }
                continue;
            }

            await workerInboxService.EnqueueProxyRotationAsync(
                account,
                oldIndex,
                account.CurrentProxyIndex,
                string.IsNullOrWhiteSpace(reason) ? "Manual or 429 Error" : reason.Trim(),
                cancellationToken);

            await hubContext.Clients.All.SendProxyRotatedEvent(accountId, account.CurrentProxyIndex);
            await hubContext.Clients.All.SendBotStatusChanged(accountId, "Proxy Rotated");

            return new ProxyRotationResult(account.CurrentProxyIndex);
        }

        throw new InvalidOperationException("Failed to rotate proxy after multiple concurrency retries.");
    }

    public async Task<AccountSummaryResponse?> MarkManualRequiredAsync(Guid accountId, string? vncUrl, CancellationToken cancellationToken = default)
    {
        var account = await LoadTrackedAccountAsync(accountId, cancellationToken);
        if (account is null)
        {
            return null;
        }

        account.Status = AccountStatus.Manual;
        account.UpdatedAtUtc = DateTime.UtcNow;
        ReplaceActiveAlert(
            account,
            stageCode: "manual_takeover",
            stageName: "Manual Takeover",
            severity: AlertSeverity.ManualRequired,
            title: "Manual takeover required",
            message: BuildManualRequiredMessage(vncUrl));
        AppendWorkflowStage(
            account,
            stageCode: "manual_takeover",
            stageName: "Manual Takeover",
            state: WorkflowStageState.ManualRequired,
            message: BuildManualRequiredMessage(vncUrl));

        await dbContext.SaveChangesAsync(cancellationToken);
        var updated = await accountService.GetAccountAsync(accountId, cancellationToken);
        await workerInboxService.EnqueueManualTakeoverAsync(
            account,
            WorkerInboxEventType.ManualTakeoverRequired,
            "Manual takeover required",
            BuildManualRequiredMessage(vncUrl),
            vncUrl,
            cancellationToken);
        await hubContext.Clients.All.SendManualRequiredEvent(accountId, vncUrl ?? string.Empty);
        await hubContext.Clients.All.SendBotStatusChanged(accountId, "ManualRequired");
        return updated;
    }

    public async Task<bool> RequestManualTakeoverAsync(Guid accountId, string vncUrl, CancellationToken cancellationToken = default)
    {
        var account = await LoadTrackedAccountAsync(accountId, cancellationToken);
        if (account is null)
        {
            return false;
        }

        account.Status = AccountStatus.Paused;
        account.UpdatedAtUtc = DateTime.UtcNow;
        ReplaceActiveAlert(
            account,
            stageCode: "manual_takeover",
            stageName: "Manual Takeover",
            severity: AlertSeverity.ManualRequired,
            title: "Manual takeover requested",
            message: BuildManualRequiredMessage(vncUrl));
        AppendWorkflowStage(
            account,
            stageCode: "manual_takeover",
            stageName: "Manual Takeover",
            state: WorkflowStageState.ManualRequired,
            message: BuildManualRequiredMessage(vncUrl));
        await dbContext.SaveChangesAsync(cancellationToken);

        await workerInboxService.EnqueueManualTakeoverAsync(
            account,
            WorkerInboxEventType.ManualTakeoverRequested,
            "Manual takeover requested",
            BuildManualRequiredMessage(vncUrl),
            vncUrl,
            cancellationToken);

        await hubContext.Clients.All.SendManualRequiredEvent(accountId, vncUrl);
        await hubContext.Clients.All.SendBotStatusChanged(accountId, "Manual Intervention Required");
        return true;
    }

    public async Task<AccountSummaryResponse?> CompleteManualTakeoverAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        var account = await LoadTrackedAccountAsync(accountId, cancellationToken);
        if (account is null)
        {
            return null;
        }

        account.Status = AccountStatus.Stable;
        account.UpdatedAtUtc = DateTime.UtcNow;
        DeactivateAlerts(account, "manual_takeover");
        AppendWorkflowStage(
            account,
            stageCode: "manual_takeover_complete",
            stageName: "Manual Takeover Complete",
            state: WorkflowStageState.Completed,
            message: "Operator completed the manual takeover. Worker is ready and waiting for the next command.");

        await dbContext.SaveChangesAsync(cancellationToken);
        await workerInboxService.AcknowledgeAccountEventsAsync(
            accountId,
            new[]
            {
                WorkerInboxEventType.ManualTakeoverRequired,
                WorkerInboxEventType.ManualTakeoverRequested
            },
            cancellationToken);

        var updated = await accountService.GetAccountAsync(accountId, cancellationToken);
        if (updated is not null)
        {
            await hubContext.Clients.All.SendBotStatusChanged(accountId, updated.Status);
        }

        return updated;
    }

    private async Task<Account?> LoadTrackedAccountAsync(Guid accountId, CancellationToken cancellationToken)
        => await dbContext.Accounts
            .Include(a => a.Alerts)
            .Include(a => a.WorkflowStages)
            .Include(a => a.Proxies)
            .Include(a => a.VpsNode)
            .FirstOrDefaultAsync(a => a.Id == accountId, cancellationToken);

    private static string BuildManualRequiredMessage(string? vncUrl)
    {
        if (string.IsNullOrWhiteSpace(vncUrl))
        {
            return "Worker requested operator takeover. Desktop can reconnect later and resume from the pending queue.";
        }

        return $"Worker requested operator takeover. VNC URL: {vncUrl.Trim()}";
    }

    private void ReplaceActiveAlert(
        Account account,
        string stageCode,
        string stageName,
        AlertSeverity severity,
        string title,
        string message)
    {
        DeactivateAlerts(account, stageCode);
        dbContext.AccountAlerts.Add(new AccountAlert
        {
            AccountId = account.Id,
            StageCode = stageCode,
            StageName = stageName,
            Severity = severity,
            Title = title,
            Message = message,
            IsActive = true
        });
    }

    private static void DeactivateAlerts(Account account, string stageCode)
    {
        foreach (var alert in account.Alerts.Where(alert => alert.IsActive && string.Equals(alert.StageCode, stageCode, StringComparison.OrdinalIgnoreCase)))
        {
            alert.IsActive = false;
            alert.AcknowledgedAtUtc = DateTime.UtcNow;
            alert.UpdatedAtUtc = DateTime.UtcNow;
        }
    }

    private void AppendWorkflowStage(
        Account account,
        string stageCode,
        string stageName,
        WorkflowStageState state,
        string message)
    {
        var nextOrder = account.WorkflowStages.Count == 0
            ? 1
            : account.WorkflowStages.Max(stage => stage.DisplayOrder) + 1;

        dbContext.AccountWorkflowStages.Add(new AccountWorkflowStage
        {
            AccountId = account.Id,
            DisplayOrder = nextOrder,
            StageCode = stageCode,
            StageName = stageName,
            State = state,
            Message = message,
            OccurredAtUtc = DateTime.UtcNow
        });
    }

    private static ProxyEntry? ParseProxyLine(Guid accountId, string line, int order)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        var parts = line.Split(':', StringSplitOptions.TrimEntries);
        if ((parts.Length != 2 && parts.Length != 4) || string.IsNullOrWhiteSpace(parts[0]))
        {
            return null;
        }

        if (!int.TryParse(parts[1], out var parsedPort) || parsedPort <= 0)
        {
            return null;
        }

        return new ProxyEntry
        {
            AccountId = accountId,
            Host = parts[0],
            Port = parsedPort,
            Username = parts.Length > 2 ? parts[2] : string.Empty,
            Password = parts.Length > 3 ? parts[3] : string.Empty,
            Order = order
        };
    }

    private static string BuildProxySignature(string host, int port, string? username, string? password)
        => $"{host.Trim()}:{port}:{username?.Trim() ?? string.Empty}:{password?.Trim() ?? string.Empty}";
}
