using Microsoft.AspNetCore.SignalR;

namespace FleetManager.Api.Hubs;

public interface IOperationsClient
{
    Task SendProxyRotatedEvent(Guid accountId, int newIndex);
    Task SendManualRequiredEvent(Guid accountId, string vncUrl);
    Task SendBotStatusChanged(Guid accountId, string status);
    Task SendNodeHeartbeatEvent(Guid nodeId, double cpu, double ram, double disk, int activeSessions, int pingMs);
    Task SendNodeStatusChanged(Guid nodeId, string status);
}

public sealed class OperationsHub : Hub<IOperationsClient>
{
    public override Task OnConnectedAsync()
    {
        return base.OnConnectedAsync();
    }
}

