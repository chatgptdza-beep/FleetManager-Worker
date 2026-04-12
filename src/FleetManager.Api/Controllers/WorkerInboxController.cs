using FleetManager.Api.Services;
using FleetManager.Contracts.Operations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FleetManager.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/worker-events")]
public sealed class WorkerInboxController(IWorkerInboxService workerInboxService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<WorkerInboxEventResponse>>> GetAsync([FromQuery] bool pendingOnly = true, CancellationToken cancellationToken = default)
        => Ok(await workerInboxService.GetEventsAsync(pendingOnly, cancellationToken));

    [HttpPost("{eventId:guid}/acknowledge")]
    public async Task<IActionResult> AcknowledgeAsync(Guid eventId, CancellationToken cancellationToken)
    {
        var acknowledged = await workerInboxService.AcknowledgeAsync(eventId, cancellationToken);
        return acknowledged ? NoContent() : NotFound();
    }
}
