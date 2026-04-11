using FleetManager.Application.Services;
using FleetManager.Contracts.Nodes;
using FleetManager.Domain.Entities;
using FleetManager.Domain.Enums;
using Xunit;

namespace FleetManager.Api.Tests;

public sealed class NodeServiceTests
{
    [Fact]
    public async Task UpdateHeartbeatAsync_updates_new_telemetry_fields()
    {
        var repository = new InMemoryNodeRepository();
        var node = new VpsNode { Name = "VPS-01", IpAddress = "10.0.0.10" };
        repository.Nodes.Add(node);
        var service = new NodeService(repository);

        await service.UpdateHeartbeatAsync(node.Id, 42, 61, 58, 16, 210, 115, 7, 9005, "Reconnecting", 9, "1.2.3");
        var summary = await service.GetNodeAsync(node.Id);

        Assert.NotNull(summary);
        Assert.Equal(16, node.RamUsedGb);
        Assert.Equal(210, node.StorageUsedGb);
        Assert.Equal(115, node.PingMs);
        Assert.Equal(9005, node.ControlPort);
        Assert.Equal("Reconnecting", node.ConnectionState);
        Assert.Equal(9, node.ConnectionTimeoutSeconds);
        Assert.Equal(NodeStatus.Online, node.Status);
        Assert.Equal(16, summary!.RamUsedGb);
        Assert.Equal(210, summary.StorageUsedGb);
        Assert.Equal(115, summary.PingMs);
    }

    [Fact]
    public async Task DispatchCommandAsync_accepts_allowlisted_command()
    {
        var repository = new InMemoryNodeRepository();
        var node = new VpsNode { Name = "VPS-01", IpAddress = "10.0.0.10" };
        repository.Nodes.Add(node);
        var service = new NodeService(repository);

        var commandId = await service.DispatchCommandAsync(node.Id, new DispatchNodeCommandRequest
        {
            CommandType = "StartAutomation",
            PayloadJson = "{\"accountId\":\"00000000-0000-0000-0000-000000000001\"}"
        });

        Assert.NotEqual(Guid.Empty, commandId);
        Assert.Single(node.Commands);
        Assert.Equal(NodeCommandType.StartAutomation, node.Commands.Single().CommandType);
    }

    [Fact]
    public async Task DispatchCommandAsync_rejects_non_allowlisted_command()
    {
        var repository = new InMemoryNodeRepository();
        var node = new VpsNode { Name = "VPS-01", IpAddress = "10.0.0.10" };
        repository.Nodes.Add(node);
        var service = new NodeService(repository);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DispatchCommandAsync(node.Id, new DispatchNodeCommandRequest
        {
            CommandType = "None",
            PayloadJson = "{}"
        }));
    }

    [Fact]
    public async Task GetNextPendingCommandAsync_marks_command_as_dispatched()
    {
        var repository = new InMemoryNodeRepository();
        var node = new VpsNode { Name = "VPS-01", IpAddress = "10.0.0.10" };
        node.Commands.Add(new NodeCommand
        {
            VpsNodeId = node.Id,
            CommandType = NodeCommandType.StartBrowser,
            PayloadJson = "{\"accountId\":\"00000000-0000-0000-0000-000000000001\"}",
            Status = CommandStatus.Pending
        });
        repository.Nodes.Add(node);
        var service = new NodeService(repository);

        var command = await service.GetNextPendingCommandAsync(node.Id);

        Assert.NotNull(command);
        Assert.Equal("StartBrowser", command!.CommandType);
        Assert.Equal(CommandStatus.Dispatched, node.Commands.Single().Status);
    }

    [Fact]
    public async Task CompleteCommandAsync_marks_command_as_executed()
    {
        var repository = new InMemoryNodeRepository();
        var node = new VpsNode { Name = "VPS-01", IpAddress = "10.0.0.10" };
        var nodeCommand = new NodeCommand
        {
            VpsNodeId = node.Id,
            CommandType = NodeCommandType.StartAutomation,
            PayloadJson = "{}",
            Status = CommandStatus.Dispatched
        };
        node.Commands.Add(nodeCommand);
        repository.Nodes.Add(node);
        var service = new NodeService(repository);

        await service.CompleteCommandAsync(node.Id, nodeCommand.Id, true, "ok");

        Assert.Equal(CommandStatus.Executed, nodeCommand.Status);
        Assert.Equal("ok", nodeCommand.ResultMessage);
        Assert.NotNull(nodeCommand.ExecutedAtUtc);
    }

    [Fact]
    public async Task GetCommandStatusAsync_returns_result_message_for_node_command()
    {
        var repository = new InMemoryNodeRepository();
        var node = new VpsNode { Name = "VPS-01", IpAddress = "10.0.0.10" };
        var nodeCommand = new NodeCommand
        {
            VpsNodeId = node.Id,
            CommandType = NodeCommandType.OpenAssignedSession,
            PayloadJson = "{}",
            Status = CommandStatus.Executed,
            ResultMessage = "Viewer ready. URL: http://10.0.0.10:6080/vnc.html"
        };

        node.Commands.Add(nodeCommand);
        repository.Nodes.Add(node);
        var service = new NodeService(repository);

        var status = await service.GetCommandStatusAsync(node.Id, nodeCommand.Id);

        Assert.NotNull(status);
        Assert.Equal(nodeCommand.Id, status!.CommandId);
        Assert.Equal("OpenAssignedSession", status.CommandType);
        Assert.Equal("Executed", status.Status);
        Assert.Equal(nodeCommand.ResultMessage, status.ResultMessage);
    }
}
