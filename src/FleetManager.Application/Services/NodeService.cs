using FleetManager.Contracts.Agent;
using FleetManager.Application.Abstractions;
using FleetManager.Contracts.Nodes;
using FleetManager.Domain.Entities;
using FleetManager.Domain.Enums;
using System.Linq;

namespace FleetManager.Application.Services;

public sealed class NodeService(INodeRepository nodeRepository) : INodeService
{
    public async Task<IReadOnlyList<NodeSummaryResponse>> GetNodesAsync(CancellationToken cancellationToken = default)
    {
        var nodes = await nodeRepository.GetAllAsync(cancellationToken);
        return nodes.Select(Map).ToList();
    }

    public async Task<NodeSummaryResponse?> GetNodeAsync(Guid nodeId, CancellationToken cancellationToken = default)
    {
        var node = await nodeRepository.GetByIdReadOnlyAsync(nodeId, cancellationToken);
        return node is null ? null : Map(node);
    }

    public async Task<NodeSummaryResponse> CreateNodeAsync(CreateNodeRequest request, CancellationToken cancellationToken = default)
    {
        var normalizedIp = NormalizeRequired(request.IpAddress, nameof(request.IpAddress));
        var existing = await nodeRepository.GetByIpAddressAsync(normalizedIp, cancellationToken);
        if (existing is not null)
        {
            throw new InvalidOperationException($"A VPS with IP address '{normalizedIp}' already exists (Name: {existing.Name}, Id: {existing.Id}).");
        }

        var node = new VpsNode
        {
            Name = NormalizeRequired(request.Name, nameof(request.Name)),
            IpAddress = NormalizeRequired(request.IpAddress, nameof(request.IpAddress)),
            SshPort = NormalizePort(request.SshPort, nameof(request.SshPort)),
            ControlPort = NormalizePort(request.ControlPort, nameof(request.ControlPort)),
            SshUsername = NormalizeRequired(request.SshUsername, nameof(request.SshUsername)),
            // Provisioning secrets are consumed during bootstrap and are not persisted server-side.
            SshPassword = null,
            SshPrivateKey = null,
            AuthType = NormalizeRequired(request.AuthType, nameof(request.AuthType)),
            OsType = NormalizeRequired(request.OsType, nameof(request.OsType)),
            Region = NormalizeOptional(request.Region),
            Status = NodeStatus.Pending,
            ConnectionState = "Disconnected",
            ConnectionTimeoutSeconds = 0
        };

        await nodeRepository.AddAsync(node, cancellationToken);
        await nodeRepository.SaveChangesAsync(cancellationToken);
        return Map(node);
    }

    public async Task<NodeSummaryResponse?> UpdateNodeStatusAsync(Guid nodeId, string status, CancellationToken cancellationToken = default)
    {
        var node = await nodeRepository.GetByIdAsync(nodeId, cancellationToken);
        if (node is null) return null;

        if (!Enum.TryParse<NodeStatus>(status, ignoreCase: true, out var newStatus))
        {
            throw new InvalidOperationException($"Invalid status value: '{status}'. Valid values: {string.Join(", ", Enum.GetNames<NodeStatus>())}.");
        }

        node.Status = newStatus;
        node.UpdatedAtUtc = DateTime.UtcNow;
        await nodeRepository.SaveChangesAsync(cancellationToken);
        return Map(node);
    }

    public async Task<bool> DeleteNodeAsync(Guid nodeId, CancellationToken cancellationToken = default)
    {
        var node = await nodeRepository.GetByIdAsync(nodeId, cancellationToken);
        if (node is null) return false;

        await nodeRepository.DeleteGraphAsync(node, cancellationToken);
        await nodeRepository.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<Guid> DispatchCommandAsync(Guid nodeId, DispatchNodeCommandRequest request, CancellationToken cancellationToken = default)
    {
        var node = await nodeRepository.GetByIdAsync(nodeId, cancellationToken)
            ?? throw new InvalidOperationException("Node not found.");

        if (!Enum.TryParse<NodeCommandType>(request.CommandType, ignoreCase: true, out var commandType))
        {
            throw new InvalidOperationException("Unsupported command type.");
        }

        if (!FleetManager.Contracts.CommandAllowlist.IsAllowed(commandType.ToString()))
        {
            throw new InvalidOperationException("Command is not in the allowlist.");
        }

        var command = new FleetManager.Domain.Entities.NodeCommand
        {
            VpsNodeId = node.Id,
            CommandType = commandType,
            PayloadJson = request.PayloadJson,
            Status = CommandStatus.Pending
        };

        await nodeRepository.AddCommandAsync(command, cancellationToken);
        await nodeRepository.SaveChangesAsync(cancellationToken);
        return command.Id;
    }

    public async Task<NodeCommandStatusResponse?> GetCommandStatusAsync(Guid nodeId, Guid commandId, CancellationToken cancellationToken = default)
    {
        var command = await nodeRepository.GetCommandByIdAsync(commandId, cancellationToken);
        if (command is null || command.VpsNodeId != nodeId)
        {
            return null;
        }

        return Map(command);
    }

    public async Task<AgentCommandEnvelopeResponse?> GetNextPendingCommandAsync(Guid nodeId, CancellationToken cancellationToken = default)
    {
        var nowUtc = DateTime.UtcNow;
        var command = await nodeRepository.ClaimNextPendingCommandAsync(
            nodeId,
            nowUtc,
            nowUtc.AddMinutes(-1),
            cancellationToken);

        if (command is null)
        {
            return null;
        }

        return new AgentCommandEnvelopeResponse
        {
            CommandId = command.Id,
            NodeId = nodeId,
            CommandType = command.CommandType.ToString(),
            PayloadJson = command.PayloadJson,
            CreatedAtUtc = command.CreatedAtUtc
        };
    }

    public async Task CompleteCommandAsync(Guid nodeId, Guid commandId, bool succeeded, string? resultMessage, CancellationToken cancellationToken = default)
    {
        var command = await nodeRepository.GetCommandByIdAsync(commandId, cancellationToken)
            ?? throw new InvalidOperationException("Command not found.");

        if (command.VpsNodeId != nodeId)
        {
            throw new InvalidOperationException("Command does not belong to the specified node.");
        }

        command.Status = succeeded ? CommandStatus.Executed : CommandStatus.Failed;
        command.ResultMessage = string.IsNullOrWhiteSpace(resultMessage) ? null : resultMessage.Trim();
        command.ExecutedAtUtc = DateTime.UtcNow;
        command.UpdatedAtUtc = DateTime.UtcNow;

        await nodeRepository.SaveChangesAsync(cancellationToken);
    }

    public async Task<NodeSummaryResponse> UpdateHeartbeatAsync(Guid nodeId, double cpuPercent, double ramPercent, double diskPercent, double ramUsedGb, double storageUsedGb, int pingMs, int activeSessions, int controlPort, string connectionState, int connectionTimeoutSeconds, string agentVersion, CancellationToken cancellationToken = default)
    {
        var node = await nodeRepository.GetByIdAsync(nodeId, cancellationToken)
            ?? throw new InvalidOperationException("Node not found.");

        var normalizedConnectionState = NormalizeConnectionState(connectionState, pingMs);

        node.LastHeartbeatAtUtc = DateTime.UtcNow;
        node.CpuPercent = cpuPercent;
        node.RamPercent = ramPercent;
        node.DiskPercent = diskPercent;
        node.RamUsedGb = ramUsedGb;
        node.StorageUsedGb = storageUsedGb;
        node.PingMs = pingMs;
        node.ActiveSessions = activeSessions;
        node.ControlPort = controlPort;
        node.ConnectionState = normalizedConnectionState;
        node.ConnectionTimeoutSeconds = connectionTimeoutSeconds;
        node.AgentVersion = string.IsNullOrWhiteSpace(agentVersion) ? null : agentVersion.Trim();
        node.Status = DetermineNodeStatus(cpuPercent, ramPercent, diskPercent, pingMs, normalizedConnectionState);
        node.UpdatedAtUtc = DateTime.UtcNow;

        await nodeRepository.SaveChangesAsync(cancellationToken);
        return Map(node);
    }

    private static NodeStatus DetermineNodeStatus(double cpuPercent, double ramPercent, double diskPercent, int pingMs, string connectionState)
    {
        if (!string.Equals(connectionState, "Connected", StringComparison.OrdinalIgnoreCase))
        {
            return NodeStatus.Degraded;
        }

        if (pingMs < 0 || cpuPercent >= 95 || ramPercent >= 95 || diskPercent >= 95)
        {
            return NodeStatus.Degraded;
        }

        return NodeStatus.Online;
    }

    private static string NormalizeConnectionState(string connectionState, int pingMs)
    {
        if (!string.IsNullOrWhiteSpace(connectionState))
        {
            return connectionState.Trim();
        }

        return pingMs < 0 ? "Degraded" : "Connected";
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

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static int NormalizePort(int port, string fieldName)
    {
        if (port is < 1 or > 65535)
        {
            throw new InvalidOperationException($"{fieldName} must be between 1 and 65535.");
        }

        return port;
    }

    private static NodeSummaryResponse Map(VpsNode node) => new()
    {
        Id = node.Id,
        Name = node.Name,
        IpAddress = node.IpAddress,
        SshPort = node.SshPort,
        SshUsername = node.SshUsername,
        AuthType = node.AuthType,
        OsType = node.OsType,
        Region = node.Region,
        Status = node.Status.ToString(),
        LastHeartbeatAtUtc = node.LastHeartbeatAtUtc,
        CpuPercent = node.CpuPercent,
        RamPercent = node.RamPercent,
        DiskPercent = node.DiskPercent,
        RamUsedGb = node.RamUsedGb,
        StorageUsedGb = node.StorageUsedGb,
        PingMs = node.PingMs,
        ActiveSessions = node.ActiveSessions,
        ControlPort = node.ControlPort,
        ConnectionState = node.ConnectionState,
        ConnectionTimeoutSeconds = node.ConnectionTimeoutSeconds,
        AssignedAccountCount = node.Accounts.Count,
        RunningAccounts = node.Accounts.Count(x => x.Status is AccountStatus.Running or AccountStatus.Stable),
        ManualAccounts = node.Accounts.Count(x => x.Status == AccountStatus.Manual),
        AlertAccounts = node.Accounts.Count(x => x.Alerts.Any(alert => alert.IsActive)),
        AgentVersion = node.AgentVersion
    };

    private static NodeCommandStatusResponse Map(NodeCommand command) => new()
    {
        CommandId = command.Id,
        NodeId = command.VpsNodeId,
        CommandType = command.CommandType.ToString(),
        Status = command.Status.ToString(),
        ResultMessage = command.ResultMessage,
        CreatedAtUtc = command.CreatedAtUtc,
        UpdatedAtUtc = command.UpdatedAtUtc,
        ExecutedAtUtc = command.ExecutedAtUtc
    };
}
