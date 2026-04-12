using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using FleetManager.Contracts.Nodes;
using FleetManager.Contracts.Operations;

namespace FleetManager.Api.Hubs;

public interface IOperationsClient
{
    Task SendProxyRotatedEvent(Guid accountId, int newIndex);
    Task SendManualRequiredEvent(Guid accountId, string vncUrl);
    Task SendBotStatusChanged(Guid accountId, string status);
    Task SendNodeHeartbeatEvent(NodeSummaryResponse node);
    Task SendNodeStatusChanged(Guid nodeId, string status);
    Task SendWorkerInboxEvent(WorkerInboxEventResponse workerEvent);
    Task RemoveWorkerInboxEvent(Guid eventId);
}

[Authorize]
public sealed class OperationsHub : Hub<IOperationsClient>
{
    public override Task OnConnectedAsync()
    {
        return base.OnConnectedAsync();
    }
}
