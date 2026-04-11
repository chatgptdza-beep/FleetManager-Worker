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
        var node = await nodeRepository.GetByIdAsync(nodeId, cancellationToken);
        return node is null ? null : Map(node);
    }

    public async Task<NodeSummaryResponse> CreateNodeAsync(CreateNodeRequest request, CancellationToken cancellationToken = default)
    {
        var node = new VpsNode
        {
            Name = request.Name,
            IpAddress = request.IpAddress,
            SshPort = request.SshPort,
            ControlPort = request.ControlPort,
            SshUsername = request.SshUsername,
            SshPassword = request.SshPassword,
            SshPrivateKey = request.SshPrivateKey,
            AuthType = request.AuthType,
            OsType = request.OsType,
            Region = request.Region,
            Status = NodeStatus.Pending
        };

        await nodeRepository.AddAsync(node, cancellationToken);
        await nodeRepository.SaveChangesAsync(cancellationToken);
        return Map(node);
    }

    public async Task<Guid> DispatchCommandAsync(Guid nodeId, DispatchNodeCommandRequest request, CancellationToken cancellationToken = default)
    {
        var node = await nodeRepository.GetByIdAsync(nodeId, cancellationToken)
            ?? throw new InvalidOperationException("Node not found.");

        if (!Enum.TryParse<NodeCommandType>(request.CommandType, ignoreCase: true, out var commandType))
        {
            throw new InvalidOperationException("Unsupported command type.");
        }

        var allowlistedCommands = new[]
        {
            NodeCommandType.StartBrowser,
            NodeCommandType.StopBrowser,
            NodeCommandType.RestartBrowserWorker,
            NodeCommandType.OpenAssignedSession,
            NodeCommandType.CloseAssignedSession,
            NodeCommandType.BringManagedWindowToFront,
            NodeCommandType.CaptureScreenshot,
            NodeCommandType.FetchSessionLogs,
            NodeCommandType.RestartAgentService,
            NodeCommandType.ReloadApprovedConfig,
            NodeCommandType.UpdateAgentPackage,
            NodeCommandType.LoginWorkflow,
            NodeCommandType.StartAutomation,
            NodeCommandType.StopAutomation,
            NodeCommandType.PauseAutomation
        };

        if (!allowlistedCommands.Contains(commandType))
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

        node.Commands.Add(command);
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
        var node = await nodeRepository.GetByIdAsync(nodeId, cancellationToken)
            ?? throw new InvalidOperationException("Node not found.");

        var nowUtc = DateTime.UtcNow;
        var command = node.Commands
            .Where(x => x.Status == CommandStatus.Pending
                || (x.Status == CommandStatus.Dispatched
                    && x.UpdatedAtUtc.HasValue
                    && x.UpdatedAtUtc.Value <= nowUtc.AddMinutes(-1)))
            .OrderBy(x => x.CreatedAtUtc)
            .FirstOrDefault();

        if (command is null)
        {
            return null;
        }

        command.Status = CommandStatus.Dispatched;
        command.UpdatedAtUtc = nowUtc;
        await nodeRepository.SaveChangesAsync(cancellationToken);

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

    public async Task UpdateHeartbeatAsync(Guid nodeId, double cpuPercent, double ramPercent, double diskPercent, double ramUsedGb, double storageUsedGb, int pingMs, int activeSessions, int controlPort, string connectionState, int connectionTimeoutSeconds, string agentVersion, CancellationToken cancellationToken = default)
    {
        var node = await nodeRepository.GetByIdAsync(nodeId, cancellationToken)
            ?? throw new InvalidOperationException("Node not found.");

        node.LastHeartbeatAtUtc = DateTime.UtcNow;
        node.CpuPercent = cpuPercent;
        node.RamPercent = ramPercent;
        node.DiskPercent = diskPercent;
        node.RamUsedGb = ramUsedGb;
        node.StorageUsedGb = storageUsedGb;
        node.PingMs = pingMs;
        node.ActiveSessions = activeSessions;
        node.ControlPort = controlPort;
        node.ConnectionState = string.IsNullOrWhiteSpace(connectionState) ? "Connected" : connectionState.Trim();
        node.ConnectionTimeoutSeconds = connectionTimeoutSeconds;
        node.AgentVersion = agentVersion;
        node.Status = NodeStatus.Online;
        node.UpdatedAtUtc = DateTime.UtcNow;

        await nodeRepository.SaveChangesAsync(cancellationToken);
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
