using FleetManager.Api.Hubs;
using FleetManager.Domain.Entities;
using FleetManager.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace FleetManager.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/accounts/{id}/proxies")]
public class ProxyController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly IHubContext<OperationsHub, IOperationsClient> _hubContext;

    public ProxyController(AppDbContext dbContext, IHubContext<OperationsHub, IOperationsClient> hubContext)
    {
        _dbContext = dbContext;
        _hubContext = hubContext;
    }

    [HttpPost("inject")]
    public async Task<IActionResult> InjectProxies(Guid id, [FromBody] InjectProxiesRequest request)
    {
        var account = await _dbContext.Accounts.Include(a => a.Proxies).FirstOrDefaultAsync(a => a.Id == id);
        if (account == null)
            return NotFound("Account not found.");

        if (string.IsNullOrWhiteSpace(request.RawProxies))
            return BadRequest("Proxy list is empty.");

        var lines = request.RawProxies.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        int order = account.Proxies.Count + 1;
        
        foreach (var line in lines)
        {
            var parts = line.Split(':');
            if (parts.Length >= 2)
            {
                var entry = new ProxyEntry
                {
                    AccountId = id,
                    Host = parts[0],
                    Port = int.TryParse(parts[1], out int p) ? p : 80,
                    Username = parts.Length > 2 ? parts[2] : string.Empty,
                    Password = parts.Length > 3 ? parts[3] : string.Empty,
                    Order = order++
                };
                _dbContext.ProxyEntries.Add(entry);
                account.Proxies.Add(entry);
            }
        }

        await _dbContext.SaveChangesAsync();
        return Ok(new { InjectedCount = lines.Length, TotalProxies = account.Proxies.Count });
    }

    [HttpPost("rotate")]
    public async Task<IActionResult> RotateProxy(Guid id, [FromBody] RotateProxyRequest request)
    {
        var account = await _dbContext.Accounts.Include(a => a.Proxies).FirstOrDefaultAsync(a => a.Id == id);
        if (account == null)
            return NotFound("Account not found.");

        if (!account.Proxies.Any())
            return BadRequest("No proxies available for rotation.");

        int oldIndex = account.CurrentProxyIndex;
        account.CurrentProxyIndex = (account.CurrentProxyIndex + 1) % account.Proxies.Count;

        var log = new ProxyRotationLog
        {
            AccountId = id,
            FromOrder = oldIndex,
            ToOrder = account.CurrentProxyIndex,
            Reason = request.Reason ?? "Manual or 429 Error",
            RotatedAtUtc = DateTime.UtcNow
        };
        _dbContext.ProxyRotationLogs.Add(log);

        await _dbContext.SaveChangesAsync();

        // Notify Desktop UI
        await _hubContext.Clients.All.SendProxyRotatedEvent(id, account.CurrentProxyIndex);
        await _hubContext.Clients.All.SendBotStatusChanged(id, "Proxy Rotated");

        return Ok(new { NewIndex = account.CurrentProxyIndex });
    }

    [HttpPost("takeover-request")]
    public async Task<IActionResult> RequestManualTakeover(Guid id, [FromBody] TakeoverRequest request)
    {
        // Called by Agent when SLOTS are found or Captcha appears
        var account = await _dbContext.Accounts.FindAsync(id);
        if (account == null) return NotFound();

        account.Status = FleetManager.Domain.Enums.AccountStatus.Paused;
        await _dbContext.SaveChangesAsync();

        await _hubContext.Clients.All.SendManualRequiredEvent(id, request.VncUrl);
        await _hubContext.Clients.All.SendBotStatusChanged(id, "Manual Intervention Required");

        return Ok();
    }
}

public class InjectProxiesRequest
{
    public string RawProxies { get; set; } = string.Empty;
}

public class RotateProxyRequest
{
    public string Reason { get; set; } = string.Empty;
}

public class TakeoverRequest
{
    public string VncUrl { get; set; } = string.Empty;
}
