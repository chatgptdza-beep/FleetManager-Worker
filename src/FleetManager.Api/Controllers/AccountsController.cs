using FleetManager.Api.Hubs;
using FleetManager.Application.Abstractions;
using FleetManager.Contracts.Accounts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace FleetManager.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/accounts")]
public sealed class AccountsController(
    IAccountService accountService,
    IHubContext<OperationsHub, IOperationsClient> hub) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AccountSummaryResponse>>> GetAsync([FromQuery] Guid? nodeId, CancellationToken cancellationToken)
        => Ok(await accountService.GetAccountsAsync(nodeId, cancellationToken));

    [HttpPost]
    public async Task<ActionResult<AccountSummaryResponse>> CreateAsync([FromBody] CreateAccountRequest request, CancellationToken cancellationToken)
    {
        var created = await accountService.CreateAccountAsync(request, cancellationToken);
        return Ok(created);
    }

    [HttpGet("{accountId:guid}/stage-alerts")]
    public async Task<ActionResult<AccountStageAlertDetailsResponse>> GetStageAlertsAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var details = await accountService.GetAccountStageAlertsAsync(accountId, cancellationToken);
        return details is null ? NotFound() : Ok(details);
    }

    [HttpPut("{accountId:guid}")]
    public async Task<ActionResult<AccountSummaryResponse>> UpdateAsync(Guid accountId, [FromBody] UpdateAccountRequest request, CancellationToken cancellationToken)
    {
        var updated = await accountService.UpdateAccountAsync(accountId, request, cancellationToken);
        if (updated is null) return NotFound();

        // Notify Desktop of status change in real time
        await hub.Clients.All.SendBotStatusChanged(accountId, updated.Status);
        return Ok(updated);
    }

    [HttpDelete("{accountId:guid}")]
    public async Task<IActionResult> DeleteAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var deleted = await accountService.DeleteAccountAsync(accountId, cancellationToken);
        return deleted ? NoContent() : NotFound();
    }

    /// <summary>
    /// Called by the VPS Agent when SLOT_FOUND is detected in container logs.
    /// Sets account status to Manual and broadcasts SignalR event to Desktop.
    /// </summary>
    [HttpPost("{accountId:guid}/manual-required")]
    [AllowAnonymous] // Agent uses API key middleware, not JWT
    public async Task<IActionResult> ManualRequiredAsync(Guid accountId, [FromBody] ManualRequiredRequest request, CancellationToken cancellationToken)
    {
        var updated = await accountService.SetAccountStatusManualAsync(accountId, cancellationToken);
        if (updated is null) return NotFound();

        await hub.Clients.All.SendManualRequiredEvent(accountId, request.VncUrl ?? string.Empty);
        await hub.Clients.All.SendBotStatusChanged(accountId, "ManualRequired");
        return Accepted();
    }
}
