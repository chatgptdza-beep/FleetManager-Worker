using System.Text.Json;
using FleetManager.Api.Hubs;
using FleetManager.Contracts.Operations;
using FleetManager.Domain.Entities;
using FleetManager.Domain.Enums;
using FleetManager.Infrastructure.Persistence;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace FleetManager.Api.Services;

public interface IWorkerInboxService
{
    Task<IReadOnlyList<WorkerInboxEventResponse>> GetEventsAsync(bool pendingOnly = true, CancellationToken cancellationToken = default);
    Task<bool> AcknowledgeAsync(Guid eventId, CancellationToken cancellationToken = default);
    Task AcknowledgeAccountEventsAsync(Guid accountId, IReadOnlyCollection<WorkerInboxEventType> eventTypes, CancellationToken cancellationToken = default);
    Task<WorkerInboxEventResponse> EnqueueManualTakeoverAsync(Account account, WorkerInboxEventType eventType, string title, string message, string? actionUrl, CancellationToken cancellationToken = default);
    Task<WorkerInboxEventResponse> EnqueueProxyRotationAsync(Account account, int fromOrder, int toOrder, string reason, CancellationToken cancellationToken = default);
    Task<WorkerInboxEventResponse?> RecordCommandFailureAsync(Guid commandId, CancellationToken cancellationToken = default);
}

public sealed class WorkerInboxService(
    AppDbContext dbContext,
    IHubContext<OperationsHub, IOperationsClient> hubContext) : IWorkerInboxService
{
    public async Task<IReadOnlyList<WorkerInboxEventResponse>> GetEventsAsync(bool pendingOnly = true, CancellationToken cancellationToken = default)
    {
        var query = dbContext.WorkerInboxEvents
            .AsNoTracking()
            .Include(workerEvent => workerEvent.Account)
            .Include(workerEvent => workerEvent.VpsNode)
            .OrderByDescending(workerEvent => workerEvent.CreatedAtUtc)
            .AsQueryable();

        if (pendingOnly)
        {
            query = query.Where(workerEvent => workerEvent.Status == WorkerInboxEventStatus.Pending);
        }

        var events = await query.ToListAsync(cancellationToken);
        return events.Select(Map).ToList();
    }

    public async Task<bool> AcknowledgeAsync(Guid eventId, CancellationToken cancellationToken = default)
    {
        var workerEvent = await dbContext.WorkerInboxEvents.FirstOrDefaultAsync(x => x.Id == eventId, cancellationToken);
        if (workerEvent is null)
        {
            return false;
        }

        if (workerEvent.Status != WorkerInboxEventStatus.Acknowledged)
        {
            workerEvent.Status = WorkerInboxEventStatus.Acknowledged;
            workerEvent.AcknowledgedAtUtc = DateTime.UtcNow;
            workerEvent.UpdatedAtUtc = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        await hubContext.Clients.All.RemoveWorkerInboxEvent(eventId);
        return true;
    }

    public async Task AcknowledgeAccountEventsAsync(Guid accountId, IReadOnlyCollection<WorkerInboxEventType> eventTypes, CancellationToken cancellationToken = default)
    {
        var pendingEvents = await dbContext.WorkerInboxEvents
            .Where(workerEvent =>
                workerEvent.AccountId == accountId
                && workerEvent.Status == WorkerInboxEventStatus.Pending
                && eventTypes.Contains(workerEvent.EventType))
            .ToListAsync(cancellationToken);

        if (pendingEvents.Count == 0)
        {
            return;
        }

        var acknowledgedAtUtc = DateTime.UtcNow;
        foreach (var workerEvent in pendingEvents)
        {
            workerEvent.Status = WorkerInboxEventStatus.Acknowledged;
            workerEvent.AcknowledgedAtUtc = acknowledgedAtUtc;
            workerEvent.UpdatedAtUtc = acknowledgedAtUtc;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        foreach (var workerEvent in pendingEvents)
        {
            await hubContext.Clients.All.RemoveWorkerInboxEvent(workerEvent.Id);
        }
    }

    public async Task<WorkerInboxEventResponse> EnqueueManualTakeoverAsync(Account account, WorkerInboxEventType eventType, string title, string message, string? actionUrl, CancellationToken cancellationToken = default)
    {
        var workerEvent = await UpsertPendingEventAsync(
            eventType,
            account.Id,
            account.VpsNodeId,
            title,
            message,
            actionUrl,
            metadataJson: null,
            deduplicatePending: true,
            cancellationToken);

        return await BroadcastAndMapAsync(workerEvent.Id, cancellationToken);
    }

    public async Task<WorkerInboxEventResponse> EnqueueProxyRotationAsync(Account account, int fromOrder, int toOrder, string reason, CancellationToken cancellationToken = default)
    {
        var metadataJson = JsonSerializer.Serialize(new
        {
            fromOrder,
            toOrder,
            reason
        });

        var workerEvent = await UpsertPendingEventAsync(
            WorkerInboxEventType.ProxyRotated,
            account.Id,
            account.VpsNodeId,
            title: "Proxy rotated automatically",
            message: $"Worker rotated the proxy from slot {fromOrder} to {toOrder}. Reason: {reason}",
            actionUrl: null,
            metadataJson,
            deduplicatePending: false,
            cancellationToken);

        return await BroadcastAndMapAsync(workerEvent.Id, cancellationToken);
    }

    public async Task<WorkerInboxEventResponse?> RecordCommandFailureAsync(Guid commandId, CancellationToken cancellationToken = default)
    {
        var command = await dbContext.NodeCommands
            .Include(nodeCommand => nodeCommand.VpsNode)
            .FirstOrDefaultAsync(nodeCommand => nodeCommand.Id == commandId, cancellationToken);
        if (command is null || command.Status != CommandStatus.Failed)
        {
            return null;
        }

        var accountId = TryExtractAccountId(command.PayloadJson);
        var account = accountId.HasValue
            ? await dbContext.Accounts.AsNoTracking().FirstOrDefaultAsync(candidate => candidate.Id == accountId.Value, cancellationToken)
            : null;

        var workerEvent = await UpsertPendingEventAsync(
            WorkerInboxEventType.CommandFailed,
            account?.Id,
            command.VpsNodeId,
            title: $"Worker command failed: {command.CommandType}",
            message: string.IsNullOrWhiteSpace(command.ResultMessage)
                ? "Worker reported a failed command with no additional details."
                : command.ResultMessage,
            actionUrl: null,
            metadataJson: JsonSerializer.Serialize(new
            {
                commandId = command.Id,
                commandType = command.CommandType.ToString()
            }),
            deduplicatePending: false,
            cancellationToken);

        return await BroadcastAndMapAsync(workerEvent.Id, cancellationToken);
    }

    private async Task<WorkerInboxEvent> UpsertPendingEventAsync(
        WorkerInboxEventType eventType,
        Guid? accountId,
        Guid? nodeId,
        string title,
        string message,
        string? actionUrl,
        string? metadataJson,
        bool deduplicatePending,
        CancellationToken cancellationToken)
    {
        WorkerInboxEvent? workerEvent = null;
        if (deduplicatePending)
        {
            workerEvent = await dbContext.WorkerInboxEvents
                .FirstOrDefaultAsync(existing =>
                    existing.Status == WorkerInboxEventStatus.Pending
                    && existing.EventType == eventType
                    && existing.AccountId == accountId,
                    cancellationToken);
        }

        if (workerEvent is null)
        {
            workerEvent = new WorkerInboxEvent
            {
                AccountId = accountId,
                VpsNodeId = nodeId,
                EventType = eventType,
                Status = WorkerInboxEventStatus.Pending,
                Title = title,
                Message = message,
                ActionUrl = string.IsNullOrWhiteSpace(actionUrl) ? null : actionUrl.Trim(),
                MetadataJson = metadataJson
            };

            dbContext.WorkerInboxEvents.Add(workerEvent);
        }
        else
        {
            workerEvent.VpsNodeId = nodeId;
            workerEvent.Title = title;
            workerEvent.Message = message;
            workerEvent.ActionUrl = string.IsNullOrWhiteSpace(actionUrl) ? null : actionUrl.Trim();
            workerEvent.MetadataJson = metadataJson;
            workerEvent.UpdatedAtUtc = DateTime.UtcNow;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return workerEvent;
    }

    private async Task<WorkerInboxEventResponse> BroadcastAndMapAsync(Guid eventId, CancellationToken cancellationToken)
    {
        var workerEvent = await dbContext.WorkerInboxEvents
            .AsNoTracking()
            .Include(x => x.Account)
            .Include(x => x.VpsNode)
            .FirstAsync(x => x.Id == eventId, cancellationToken);

        var payload = Map(workerEvent);
        await hubContext.Clients.All.SendWorkerInboxEvent(payload);
        return payload;
    }

    private static Guid? TryExtractAccountId(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            if (document.RootElement.TryGetProperty("accountId", out var accountIdElement)
                && accountIdElement.ValueKind == JsonValueKind.String
                && Guid.TryParse(accountIdElement.GetString(), out var accountId))
            {
                return accountId;
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static WorkerInboxEventResponse Map(WorkerInboxEvent workerEvent) => new()
    {
        Id = workerEvent.Id,
        EventType = workerEvent.EventType.ToString(),
        Status = workerEvent.Status.ToString(),
        Title = workerEvent.Title,
        Message = workerEvent.Message,
        ActionUrl = workerEvent.ActionUrl,
        AccountId = workerEvent.AccountId,
        AccountEmail = workerEvent.Account?.Email,
        NodeId = workerEvent.VpsNodeId,
        NodeName = workerEvent.VpsNode?.Name,
        CreatedAtUtc = workerEvent.CreatedAtUtc,
        UpdatedAtUtc = workerEvent.UpdatedAtUtc,
        AcknowledgedAtUtc = workerEvent.AcknowledgedAtUtc
    };
}
