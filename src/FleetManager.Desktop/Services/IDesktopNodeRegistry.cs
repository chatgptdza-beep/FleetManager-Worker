using FleetManager.Contracts.Accounts;
using FleetManager.Contracts.Nodes;
using FleetManager.Desktop.Models;

namespace FleetManager.Desktop.Services;

public interface IDesktopNodeRegistry
{
    Task<IReadOnlyList<DesktopManagedNodeRecord>> GetNodesAsync(CancellationToken cancellationToken = default);
    Task<DesktopManagedNodeRecord?> GetByRemoteNodeIdAsync(Guid remoteNodeId, CancellationToken cancellationToken = default);
    Task<DesktopManagedNodeRecord?> GetByWorkflowNodeIdAsync(Guid workflowNodeId, CancellationToken cancellationToken = default);
    Task<DesktopManagedNodeRecord> UpsertProvisionedNodeAsync(CreateNodeRequest request, NodeSummaryResponse remoteNode, string? apiBaseUrl, CancellationToken cancellationToken = default);
    Task SyncRemoteNodesAsync(IReadOnlyList<NodeSummaryResponse> remoteNodes, string? apiBaseUrl, CancellationToken cancellationToken = default);
    Task SyncTaskDataAsync(IReadOnlyList<AccountSummaryResponse> accounts, CancellationToken cancellationToken = default);
    Task<bool> RemoveByRemoteNodeIdAsync(Guid remoteNodeId, CancellationToken cancellationToken = default);
    Task UpdateNodeHealthAsync(Guid workflowNodeId, DesktopManagedNodeStatus status, string? statusMessage, DateTime checkedAtUtc, DateTime? sshSuccessAtUtc = null, DateTime? healedAtUtc = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the connection endpoint and SSH credentials of a locally-registered node.
    /// Used by the Node Registry Editor when the operator changes a VPS IP or rotates SSH keys.
    /// </summary>
    Task<DesktopManagedNodeRecord> UpdateNodeConnectionAsync(
        Guid workflowNodeId,
        string name,
        string currentIp,
        int sshPort,
        string sshUsername,
        string? sshPassword,
        string? sshPrivateKey,
        string authType,
        int controlPort,
        string? region,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a node from the local desktop registry by its workflow node ID.
    /// Does NOT affect the remote API — only the local tracking file.
    /// </summary>
    Task<bool> RemoveByWorkflowNodeIdAsync(Guid workflowNodeId, CancellationToken cancellationToken = default);

    CreateNodeRequest BuildConnectionRequest(DesktopManagedNodeRecord node);
}
