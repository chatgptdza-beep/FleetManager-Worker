using FleetManager.Contracts.Agent;
using FleetManager.Contracts.Nodes;

namespace FleetManager.Application.Abstractions;

public interface INodeService
{
    Task<IReadOnlyList<NodeSummaryResponse>> GetNodesAsync(CancellationToken cancellationToken = default);
    Task<NodeSummaryResponse?> GetNodeAsync(Guid nodeId, CancellationToken cancellationToken = default);
    Task<NodeSummaryResponse> CreateNodeAsync(CreateNodeRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteNodeAsync(Guid nodeId, CancellationToken cancellationToken = default);
    Task<Guid> DispatchCommandAsync(Guid nodeId, DispatchNodeCommandRequest request, CancellationToken cancellationToken = default);
    Task<NodeCommandStatusResponse?> GetCommandStatusAsync(Guid nodeId, Guid commandId, CancellationToken cancellationToken = default);
    Task<AgentCommandEnvelopeResponse?> GetNextPendingCommandAsync(Guid nodeId, CancellationToken cancellationToken = default);
    Task CompleteCommandAsync(Guid nodeId, Guid commandId, bool succeeded, string? resultMessage, CancellationToken cancellationToken = default);
    Task<NodeSummaryResponse> UpdateHeartbeatAsync(Guid nodeId, double cpuPercent, double ramPercent, double diskPercent, double ramUsedGb, double storageUsedGb, int pingMs, int activeSessions, int controlPort, string connectionState, int connectionTimeoutSeconds, string agentVersion, CancellationToken cancellationToken = default);
}
