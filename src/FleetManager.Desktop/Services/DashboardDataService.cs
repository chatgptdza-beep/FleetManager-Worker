using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FleetManager.Contracts.Accounts;
using FleetManager.Contracts.Nodes;
using FleetManager.Contracts.Operations;

namespace FleetManager.Desktop.Services;

public sealed class DashboardDataService : IDashboardDataService
{
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _authLock = new(1, 1);
    private string? _bearerToken;

    public DashboardDataService()
    {
        CurrentBaseUrl = NormalizeBaseUrl(Environment.GetEnvironmentVariable("FLEETMANAGER_API_BASE_URL") ?? "http://localhost:5188/");
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(CurrentBaseUrl, UriKind.Absolute),
            Timeout = TimeSpan.FromSeconds(10)
        };
    }

    public string CurrentModeLabel => "API mode";
    public string CurrentBaseUrl { get; private set; }
    public string? BearerToken => _bearerToken;

    public void ConfigureBaseUrl(string baseUrl)
    {
        CurrentBaseUrl = NormalizeBaseUrl(baseUrl);
        _httpClient.BaseAddress = new Uri(CurrentBaseUrl, UriKind.Absolute);
        _httpClient.DefaultRequestHeaders.Authorization = null;
        _bearerToken = null;
    }

    public async Task<IReadOnlyList<NodeSummaryResponse>> GetNodesAsync(CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);
        var response = await _httpClient.GetFromJsonAsync<List<NodeSummaryResponse>>("api/nodes", cancellationToken)
            ?? new List<NodeSummaryResponse>();
        return response;
    }

    public async Task<NodeSummaryResponse?> GetNodeAsync(Guid nodeId, CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);
        using var response = await _httpClient.GetAsync($"api/nodes/{nodeId}", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<NodeSummaryResponse>(cancellationToken: cancellationToken);
    }

    public async Task<NodeSummaryResponse> CreateNodeAsync(CreateNodeRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);
        using var response = await _httpClient.PostAsJsonAsync("api/nodes", request, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Duplicate VPS: {body}");
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<NodeSummaryResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Node creation returned no payload.");
    }

    public async Task<NodeSummaryResponse?> UpdateNodeStatusAsync(Guid nodeId, string status, CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);
        var request = new HttpRequestMessage(HttpMethod.Patch, $"api/nodes/{nodeId}/status")
        {
            Content = JsonContent.Create(new { Status = status })
        };
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<NodeSummaryResponse>(cancellationToken: cancellationToken);
    }

    public async Task<IReadOnlyList<AccountSummaryResponse>> GetAccountsAsync(Guid? nodeId = null, CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);
        var relativeUrl = nodeId.HasValue
            ? $"api/accounts?nodeId={nodeId.Value}"
            : "api/accounts";
        var response = await _httpClient.GetFromJsonAsync<List<AccountSummaryResponse>>(relativeUrl, cancellationToken)
            ?? new List<AccountSummaryResponse>();
        return response;
    }

    public async Task<AccountSummaryResponse?> GetAccountAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);
        using var response = await _httpClient.GetAsync($"api/accounts/{accountId}", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AccountSummaryResponse>(cancellationToken: cancellationToken);
    }

    public async Task<AccountStageAlertDetailsResponse?> GetAccountStageAlertsAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);
        using var response = await _httpClient.GetAsync($"api/accounts/{accountId}/stage-alerts", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AccountStageAlertDetailsResponse>(cancellationToken: cancellationToken);
    }

    public async Task<AccountSummaryResponse?> CompleteManualTakeoverAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);
        using var response = await _httpClient.PostAsync($"api/accounts/{accountId}/manual-complete", content: null, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AccountSummaryResponse>(cancellationToken: cancellationToken);
    }

    public async Task<InjectProxiesResponse> InjectProxiesAsync(Guid accountId, string rawProxies, bool replaceExisting = false, CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);
        using var response = await _httpClient.PostAsJsonAsync(
            $"api/accounts/{accountId}/proxies/inject",
            new
            {
                RawProxies = rawProxies,
                ReplaceExisting = replaceExisting
            },
            cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException("Account not found.");
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<InjectProxiesResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Proxy injection returned no payload.");
    }

    public async Task<RotateProxyResponse> RotateProxyAsync(Guid accountId, string? reason = null, CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);
        using var response = await _httpClient.PostAsJsonAsync(
            $"api/accounts/{accountId}/proxies/rotate",
            new { Reason = reason ?? "Manual rotation from Desktop" },
            cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException("Account not found.");
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<RotateProxyResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Proxy rotation returned no payload.");
    }

    public async Task<IReadOnlyList<WorkerInboxEventResponse>> GetWorkerInboxEventsAsync(bool pendingOnly = true, CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);
        var relativeUrl = pendingOnly ? "api/worker-events?pendingOnly=true" : "api/worker-events?pendingOnly=false";
        var response = await _httpClient.GetFromJsonAsync<List<WorkerInboxEventResponse>>(relativeUrl, cancellationToken)
            ?? new List<WorkerInboxEventResponse>();
        return response;
    }

    public async Task<bool> AcknowledgeWorkerInboxEventAsync(Guid eventId, CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);
        using var response = await _httpClient.PostAsync($"api/worker-events/{eventId}/acknowledge", content: null, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }

        response.EnsureSuccessStatusCode();
        return true;
    }

    public async Task<AccountSummaryResponse> CreateAccountAsync(CreateAccountRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);
        using var response = await _httpClient.PostAsJsonAsync("api/accounts", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AccountSummaryResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Account creation returned no payload.");
    }

    public async Task<AccountSummaryResponse?> UpdateAccountAsync(Guid accountId, UpdateAccountRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);
        using var response = await _httpClient.PutAsJsonAsync($"api/accounts/{accountId}", request, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(body)
                ? $"Failed to update account. HTTP {(int)response.StatusCode}."
                : body);
        }

        return await response.Content.ReadFromJsonAsync<AccountSummaryResponse>(cancellationToken: cancellationToken);
    }

    public async Task<bool> DeleteAccountAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);
        using var response = await _httpClient.DeleteAsync($"api/accounts/{accountId}", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }

        response.EnsureSuccessStatusCode();
        return true;
    }

    public async Task<bool> DeleteNodeAsync(Guid nodeId, CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);
        using var response = await _httpClient.DeleteAsync($"api/nodes/{nodeId}", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }

        response.EnsureSuccessStatusCode();
        return true;
    }

    public async Task<Guid?> DispatchNodeCommandAsync(Guid nodeId, DispatchNodeCommandRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);
        using var response = await _httpClient.PostAsJsonAsync($"api/nodes/{nodeId}/commands", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<DispatchResponse>(cancellationToken: cancellationToken);
        return payload?.CommandId;
    }

    public async Task<NodeCommandStatusResponse?> GetNodeCommandStatusAsync(Guid nodeId, Guid commandId, CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);
        using var response = await _httpClient.GetAsync($"api/nodes/{nodeId}/commands/{commandId}", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<NodeCommandStatusResponse>(cancellationToken: cancellationToken);
    }

    private async Task EnsureAuthenticatedAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_bearerToken))
        {
            return;
        }

        await _authLock.WaitAsync(cancellationToken);
        try
        {
            if (!string.IsNullOrWhiteSpace(_bearerToken))
            {
                return;
            }

            using var response = await _httpClient.PostAsJsonAsync(
                "api/auth/token",
                new { Password = ResolveOperatorPassword() },
                cancellationToken);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
            if (!result.TryGetProperty("token", out var tokenElement))
            {
                throw new InvalidOperationException("Authentication response did not include a token.");
            }

            _bearerToken = tokenElement.GetString();
            if (string.IsNullOrWhiteSpace(_bearerToken))
            {
                throw new InvalidOperationException("Authentication response returned an empty token.");
            }

            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _bearerToken);
        }
        finally
        {
            _authLock.Release();
        }
    }

    private string ResolveOperatorPassword()
    {
        var configuredPassword = Environment.GetEnvironmentVariable("FLEETMANAGER_API_PASSWORD");
        if (!string.IsNullOrWhiteSpace(configuredPassword))
        {
            return configuredPassword.Trim();
        }

        if (Uri.TryCreate(CurrentBaseUrl, UriKind.Absolute, out var baseUri)
            && (string.Equals(baseUri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
                || string.Equals(baseUri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase)))
        {
            return "Admin@FleetMgr2026!";
        }

        throw new InvalidOperationException(
            "Missing FLEETMANAGER_API_PASSWORD for the configured API endpoint.");
    }

    private static string NormalizeBaseUrl(string baseUrl)
        => baseUrl.EndsWith("/", StringComparison.Ordinal) ? baseUrl : $"{baseUrl}/";

    private sealed class DispatchResponse
    {
        public Guid CommandId { get; set; }
    }
}
