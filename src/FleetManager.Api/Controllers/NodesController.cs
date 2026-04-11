using FleetManager.Application.Abstractions;
using FleetManager.Contracts.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FleetManager.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/nodes")]
public sealed class NodesController(INodeService nodeService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<NodeSummaryResponse>>> GetAsync(CancellationToken cancellationToken)
        => Ok(await nodeService.GetNodesAsync(cancellationToken));

    [HttpGet("{nodeId:guid}")]
    public async Task<ActionResult<NodeSummaryResponse>> GetByIdAsync(Guid nodeId, CancellationToken cancellationToken)
    {
        var node = await nodeService.GetNodeAsync(nodeId, cancellationToken);
        return node is null ? NotFound() : Ok(node);
    }

    [HttpPost]
    public async Task<ActionResult<NodeSummaryResponse>> CreateAsync([FromBody] CreateNodeRequest request, CancellationToken cancellationToken)
    {
        var created = await nodeService.CreateNodeAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetByIdAsync), new { nodeId = created.Id }, created);
    }

    [HttpGet("{nodeId:guid}/commands/{commandId:guid}")]
    public async Task<ActionResult<NodeCommandStatusResponse>> GetCommandStatusAsync(Guid nodeId, Guid commandId, CancellationToken cancellationToken)
    {
        var command = await nodeService.GetCommandStatusAsync(nodeId, commandId, cancellationToken);
        return command is null ? NotFound() : Ok(command);
    }

    [HttpPost("{nodeId:guid}/commands")]
    public async Task<ActionResult<object>> DispatchCommandAsync(Guid nodeId, [FromBody] DispatchNodeCommandRequest request, CancellationToken cancellationToken)
    {
        var commandId = await nodeService.DispatchCommandAsync(nodeId, request, cancellationToken);
        return Accepted(new { commandId });
    }
}
