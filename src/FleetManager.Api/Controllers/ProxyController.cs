using FleetManager.Api.Hubs;
using FleetManager.Api.Services;
using FleetManager.Application.Abstractions;
using FleetManager.Contracts.Accounts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace FleetManager.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/accounts/{id}/proxies")]
public sealed class ProxyController(
    IAccountAutomationCoordinator automationCoordinator,
    IAccountService accountService,
    IHubContext<OperationsHub, IOperationsClient> hub) : ControllerBase
{
    [HttpPost("inject")]
    public async Task<IActionResult> InjectProxies(Guid id, [FromBody] InjectProxiesRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        try
        {
            var result = await automationCoordinator.InjectProxiesAsync(id, request.RawProxies, request.ReplaceExisting, cancellationToken);
            if (result is null)
            {
                return NotFound("Account not found.");
            }

            var updated = await accountService.GetAccountAsync(id, cancellationToken);
            if (updated is not null)
            {
                await hub.Clients.All.SendBotStatusChanged(id, updated.Status);
            }

            return Ok(new InjectProxiesResponse
            {
                InjectedCount = result.InjectedCount,
                TotalProxies = result.TotalProxies,
                ClearedCount = result.ClearedCount
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("rotate")]
    [AllowAnonymous]
    public async Task<IActionResult> RotateProxy(Guid id, [FromBody] RotateProxyRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await automationCoordinator.RotateProxyAsync(id, request.Reason, cancellationToken);
            if (result is null)
            {
                return NotFound("Account not found.");
            }

            return Ok(new RotateProxyResponse
            {
                NewIndex = result.NewIndex
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("takeover-request")]
    public async Task<IActionResult> RequestManualTakeover(Guid id, [FromBody] TakeoverRequest request, CancellationToken cancellationToken)
    {
        var updated = await automationCoordinator.RequestManualTakeoverAsync(id, request.VncUrl, cancellationToken);
        return updated ? Ok() : NotFound();
    }
}

public sealed class InjectProxiesRequest
{
    /// <summary>One proxy per line. Formats: ip:port or ip:port:user:password. Max 200 KB.</summary>
    [System.ComponentModel.DataAnnotations.Required]
    [System.ComponentModel.DataAnnotations.MaxLength(200_000, ErrorMessage = "Proxy list must not exceed 200 KB.")]
    public string RawProxies { get; set; } = string.Empty;

    public bool ReplaceExisting { get; set; }
}

public sealed class RotateProxyRequest
{
    [System.ComponentModel.DataAnnotations.MaxLength(500, ErrorMessage = "Reason must not exceed 500 characters.")]
    public string Reason { get; set; } = string.Empty;
}

public sealed class TakeoverRequest
{
    [System.ComponentModel.DataAnnotations.Required]
    [System.ComponentModel.DataAnnotations.MaxLength(2048, ErrorMessage = "VNC URL must not exceed 2048 characters.")]
    [System.ComponentModel.DataAnnotations.Url(ErrorMessage = "VNC URL must be a valid URL.")]
    public string VncUrl { get; set; } = string.Empty;
}
