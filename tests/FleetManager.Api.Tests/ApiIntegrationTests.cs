using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FleetManager.Contracts.Accounts;
using FleetManager.Contracts.Operations;
using FleetManager.Domain.Entities;
using FleetManager.Domain.Enums;
using FleetManager.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FleetManager.Api.Tests;

public sealed class ApiIntegrationTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    [Fact]
    public async Task Health_endpoint_is_public()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Token_endpoint_returns_jwt()
    {
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/auth/token", new { password = "Admin@FleetMgr2026!" });
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(payload.TryGetProperty("token", out var token));
        Assert.False(string.IsNullOrWhiteSpace(token.GetString()));
    }

    [Fact]
    public async Task Protected_nodes_endpoint_requires_operator_jwt()
    {
        using var client = factory.CreateClient();

        using var unauthorizedResponse = await client.GetAsync("/api/nodes");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorizedResponse.StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await GetTokenAsync(client));

        using var authorizedResponse = await client.GetAsync("/api/nodes");
        Assert.Equal(HttpStatusCode.OK, authorizedResponse.StatusCode);
    }

    [Fact]
    public async Task Protected_account_endpoint_returns_summary_with_operator_jwt()
    {
        var accountId = await SeedAccountAsync(withProxies: false);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await GetTokenAsync(client));

        var payload = await client.GetFromJsonAsync<AccountSummaryResponse>($"/api/accounts/{accountId}");

        Assert.NotNull(payload);
        Assert.Equal(accountId, payload!.Id);
        Assert.Equal("operator", payload.Username);
        Assert.Equal(0, payload.ProxyCount);
        Assert.Equal(0, payload.CurrentProxyIndex);
    }

    [Fact]
    public async Task Inject_proxies_endpoint_accepts_operator_jwt_and_updates_account_summary()
    {
        var accountId = await SeedAccountAsync(withProxies: false);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await GetTokenAsync(client));

        using var injectResponse = await client.PostAsJsonAsync(
            $"/api/accounts/{accountId}/proxies/inject",
            new
            {
                rawProxies = "10.0.0.10:9000:user1:pass1\r\n10.0.0.11:9001"
            });
        injectResponse.EnsureSuccessStatusCode();

        var injectPayload = await injectResponse.Content.ReadFromJsonAsync<InjectProxiesResponse>();
        Assert.NotNull(injectPayload);
        Assert.Equal(2, injectPayload!.InjectedCount);
        Assert.Equal(2, injectPayload.TotalProxies);
        Assert.Equal(0, injectPayload.ClearedCount);

        var summary = await client.GetFromJsonAsync<AccountSummaryResponse>($"/api/accounts/{accountId}");
        Assert.NotNull(summary);
        Assert.Equal(2, summary!.ProxyCount);
        Assert.Equal(0, summary.CurrentProxyIndex);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var proxyOrders = dbContext.ProxyEntries
            .Where(entry => entry.AccountId == accountId)
            .OrderBy(entry => entry.Order)
            .Select(entry => new { entry.Order, entry.Host, entry.Port, entry.Username, entry.Password })
            .ToList();
        Assert.Collection(proxyOrders,
            first =>
            {
                Assert.Equal(0, first.Order);
                Assert.Equal("10.0.0.10", first.Host);
                Assert.Equal(9000, first.Port);
                Assert.Equal("user1", first.Username);
                Assert.Equal("pass1", first.Password);
            },
            second =>
            {
                Assert.Equal(1, second.Order);
                Assert.Equal("10.0.0.11", second.Host);
                Assert.Equal(9001, second.Port);
                Assert.Equal(string.Empty, second.Username);
                Assert.Equal(string.Empty, second.Password);
            });
    }

    [Fact]
    public async Task Inject_proxies_endpoint_rejects_invalid_proxy_formats()
    {
        var accountId = await SeedAccountAsync(withProxies: false);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await GetTokenAsync(client));

        using var injectResponse = await client.PostAsJsonAsync(
            $"/api/accounts/{accountId}/proxies/inject",
            new
            {
                rawProxies = "10.0.0.10\r\n10.0.0.11:user-only\r\n10.0.0.12:abc"
            });

        Assert.Equal(HttpStatusCode.BadRequest, injectResponse.StatusCode);
        var error = await injectResponse.Content.ReadAsStringAsync();
        Assert.Contains("ip:port", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Inject_proxies_endpoint_can_replace_existing_proxy_pool()
    {
        var accountId = await SeedAccountAsync(withProxies: true);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await GetTokenAsync(client));

        using var injectResponse = await client.PostAsJsonAsync(
            $"/api/accounts/{accountId}/proxies/inject",
            new
            {
                rawProxies = "10.10.10.10:9100:newuser:newpass",
                replaceExisting = true
            });
        injectResponse.EnsureSuccessStatusCode();

        var injectPayload = await injectResponse.Content.ReadFromJsonAsync<InjectProxiesResponse>();
        Assert.NotNull(injectPayload);
        Assert.Equal(1, injectPayload!.InjectedCount);
        Assert.Equal(1, injectPayload.TotalProxies);
        Assert.Equal(2, injectPayload.ClearedCount);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var proxies = dbContext.ProxyEntries.Where(entry => entry.AccountId == accountId).OrderBy(entry => entry.Order).ToList();
        var proxy = Assert.Single(proxies);
        Assert.Equal("10.10.10.10", proxy.Host);
        Assert.Equal(9100, proxy.Port);
        Assert.Equal(0, proxy.Order);
    }

    [Fact]
    public async Task Manual_required_endpoint_accepts_machine_api_key()
    {
        var accountId = await SeedAccountAsync(withProxies: false);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "TEST-AGENT-KEY");

        using var response = await client.PostAsJsonAsync(
            $"/api/accounts/{accountId}/manual-required",
            new { vncUrl = "http://10.0.0.21:9001/vnc.html" });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var account = await dbContext.Accounts.FindAsync(accountId);
        Assert.NotNull(account);
        Assert.Equal(AccountStatus.Manual, account!.Status);
        Assert.Contains(dbContext.AccountAlerts, alert =>
            alert.AccountId == accountId
            && alert.IsActive
            && alert.StageCode == "manual_takeover"
            && alert.Message.Contains("http://10.0.0.21:9001/vnc.html", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Worker_inbox_returns_pending_manual_takeover_event_for_operator()
    {
        var accountId = await SeedAccountAsync(withProxies: false);

        using (var machineClient = factory.CreateClient())
        {
            machineClient.DefaultRequestHeaders.Add("X-Api-Key", "TEST-AGENT-KEY");
            using var manualResponse = await machineClient.PostAsJsonAsync(
                $"/api/accounts/{accountId}/manual-required",
                new { vncUrl = "http://10.0.0.21:9001/vnc.html" });
            manualResponse.EnsureSuccessStatusCode();
        }

        using var operatorClient = factory.CreateClient();
        operatorClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await GetTokenAsync(operatorClient));

        var workerEvents = await operatorClient.GetFromJsonAsync<List<WorkerInboxEventResponse>>("/api/worker-events");

        Assert.NotNull(workerEvents);
        var workerEvent = Assert.Single(workerEvents!.Where(candidate =>
            candidate.AccountId == accountId
            && candidate.EventType == WorkerInboxEventType.ManualTakeoverRequired.ToString()));
        Assert.Equal(WorkerInboxEventStatus.Pending.ToString(), workerEvent.Status);
        Assert.Equal("http://10.0.0.21:9001/vnc.html", workerEvent.ActionUrl);
    }

    [Fact]
    public async Task Manual_complete_endpoint_clears_pending_manual_alert_and_leaves_account_waiting()
    {
        var accountId = await SeedAccountAsync(withProxies: false);

        using (var machineClient = factory.CreateClient())
        {
            machineClient.DefaultRequestHeaders.Add("X-Api-Key", "TEST-AGENT-KEY");
            using var manualResponse = await machineClient.PostAsJsonAsync(
                $"/api/accounts/{accountId}/manual-required",
                new { vncUrl = "http://10.0.0.21:9001/vnc.html" });
            manualResponse.EnsureSuccessStatusCode();
        }

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await GetTokenAsync(client));

        using var response = await client.PostAsync($"/api/accounts/{accountId}/manual-complete", content: null);
        response.EnsureSuccessStatusCode();

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var account = await dbContext.Accounts.FindAsync(accountId);
        Assert.NotNull(account);
        Assert.Equal(AccountStatus.Stable, account!.Status);
        Assert.DoesNotContain(dbContext.AccountAlerts, alert =>
            alert.AccountId == accountId
            && alert.IsActive
            && alert.StageCode == "manual_takeover");
        Assert.Contains(dbContext.AccountWorkflowStages, stage =>
            stage.AccountId == accountId
            && stage.StageCode == "manual_takeover_complete");
    }

    [Fact]
    public async Task Manual_complete_endpoint_removes_pending_worker_inbox_event()
    {
        var accountId = await SeedAccountAsync(withProxies: false);

        using (var machineClient = factory.CreateClient())
        {
            machineClient.DefaultRequestHeaders.Add("X-Api-Key", "TEST-AGENT-KEY");
            using var manualResponse = await machineClient.PostAsJsonAsync(
                $"/api/accounts/{accountId}/manual-required",
                new { vncUrl = "http://10.0.0.21:9001/vnc.html" });
            manualResponse.EnsureSuccessStatusCode();
        }

        using var operatorClient = factory.CreateClient();
        operatorClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await GetTokenAsync(operatorClient));

        using var completeResponse = await operatorClient.PostAsync($"/api/accounts/{accountId}/manual-complete", content: null);
        completeResponse.EnsureSuccessStatusCode();

        var pendingEvents = await operatorClient.GetFromJsonAsync<List<WorkerInboxEventResponse>>("/api/worker-events");

        Assert.NotNull(pendingEvents);
        Assert.DoesNotContain(pendingEvents!, candidate =>
            candidate.AccountId == accountId
            && (candidate.EventType == WorkerInboxEventType.ManualTakeoverRequired.ToString()
                || candidate.EventType == WorkerInboxEventType.ManualTakeoverRequested.ToString()));
    }

    [Fact]
    public async Task Worker_inbox_acknowledge_endpoint_marks_event_as_acknowledged()
    {
        var accountId = await SeedAccountAsync(withProxies: false);

        using (var machineClient = factory.CreateClient())
        {
            machineClient.DefaultRequestHeaders.Add("X-Api-Key", "TEST-AGENT-KEY");
            using var manualResponse = await machineClient.PostAsJsonAsync(
                $"/api/accounts/{accountId}/manual-required",
                new { vncUrl = "http://10.0.0.21:9001/vnc.html" });
            manualResponse.EnsureSuccessStatusCode();
        }

        using var operatorClient = factory.CreateClient();
        operatorClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await GetTokenAsync(operatorClient));

        var pendingEvents = await operatorClient.GetFromJsonAsync<List<WorkerInboxEventResponse>>("/api/worker-events");
        var workerEvent = Assert.Single(pendingEvents!.Where(candidate =>
            candidate.AccountId == accountId
            && candidate.EventType == WorkerInboxEventType.ManualTakeoverRequired.ToString()));

        using var acknowledgeResponse = await operatorClient.PostAsync($"/api/worker-events/{workerEvent.Id}/acknowledge", content: null);
        Assert.Equal(HttpStatusCode.NoContent, acknowledgeResponse.StatusCode);

        var openEvents = await operatorClient.GetFromJsonAsync<List<WorkerInboxEventResponse>>("/api/worker-events");
        Assert.DoesNotContain(openEvents!, candidate => candidate.Id == workerEvent.Id);
    }

    [Fact]
    public async Task Rotate_proxy_endpoint_accepts_machine_api_key_and_logs_rotation()
    {
        var accountId = await SeedAccountAsync(withProxies: true);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "TEST-AGENT-KEY");

        using var response = await client.PostAsJsonAsync(
            $"/api/accounts/{accountId}/proxies/rotate",
            new { reason = "429 Rate Limit" });

        response.EnsureSuccessStatusCode();

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var account = await dbContext.Accounts.FindAsync(accountId);
        Assert.NotNull(account);
        Assert.Equal(1, account!.CurrentProxyIndex);
        Assert.Single(dbContext.ProxyRotationLogs.Where(log => log.AccountId == accountId));
        Assert.Contains(dbContext.WorkerInboxEvents, workerEvent =>
            workerEvent.AccountId == accountId
            && workerEvent.EventType == WorkerInboxEventType.ProxyRotated
            && workerEvent.Status == WorkerInboxEventStatus.Pending);
    }

    [Fact]
    public async Task Rotate_proxy_endpoint_accepts_operator_jwt_and_exposes_rotation_history()
    {
        var accountId = await SeedAccountAsync(withProxies: true);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await GetTokenAsync(client));

        using var rotateResponse = await client.PostAsJsonAsync(
            $"/api/accounts/{accountId}/proxies/rotate",
            new { reason = "Desktop rotate" });
        rotateResponse.EnsureSuccessStatusCode();

        var payload = await rotateResponse.Content.ReadFromJsonAsync<RotateProxyResponse>();
        Assert.NotNull(payload);
        Assert.Equal(1, payload!.NewIndex);

        var details = await client.GetFromJsonAsync<AccountStageAlertDetailsResponse>($"/api/accounts/{accountId}/stage-alerts");
        Assert.NotNull(details);
        var rotation = Assert.Single(details!.ProxyRotations);
        Assert.Equal(0, rotation.FromOrder);
        Assert.Equal(1, rotation.ToOrder);
        Assert.Equal("Desktop rotate", rotation.Reason);
    }

    private static async Task<string> GetTokenAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync("/api/auth/token", new { password = "Admin@FleetMgr2026!" });
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        return payload.GetProperty("token").GetString() ?? throw new InvalidOperationException("Token was empty.");
    }

    private async Task<Guid> SeedAccountAsync(bool withProxies)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var node = new VpsNode
        {
            Name = $"Node-{Guid.NewGuid():N}",
            IpAddress = "10.0.0.21",
            SshPort = 22,
            SshUsername = "fleetmgr",
            AuthType = "SshKey",
            OsType = "Ubuntu",
            Status = NodeStatus.Online,
            ConnectionState = "Connected"
        };

        var account = new Account
        {
            Email = $"account-{Guid.NewGuid():N}@example.com",
            Username = "operator",
            Status = AccountStatus.Running,
            VpsNode = node,
            CurrentStageCode = "ready",
            CurrentStageName = "Ready"
        };

        if (withProxies)
        {
            account.Proxies.Add(new ProxyEntry
            {
                Host = "1.1.1.1",
                Port = 8000,
                Username = "proxy1",
                Password = "secret1",
                Order = 0
            });
            account.Proxies.Add(new ProxyEntry
            {
                Host = "2.2.2.2",
                Port = 8001,
                Username = "proxy2",
                Password = "secret2",
                Order = 1
            });
        }

        dbContext.VpsNodes.Add(node);
        dbContext.Accounts.Add(account);
        await dbContext.SaveChangesAsync();
        return account.Id;
    }
}
