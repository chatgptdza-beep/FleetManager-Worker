using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FleetManager.Contracts.Accounts;
using FleetManager.Contracts.Nodes;

namespace FleetManager.Desktop.Services;

public sealed class DashboardDataService : IDashboardDataService
{
    private readonly HttpClient _httpClient;
    private readonly OfflineDashboardDataService _fallback = new();
    private string? _bearerToken;
    private readonly SemaphoreSlim _authLock = new(1, 1);

    public DashboardDataService()
    {
        CurrentBaseUrl = NormalizeBaseUrl(Environment.GetEnvironmentVariable("FLEETMANAGER_API_BASE_URL") ?? "http://82.223.9.98:5000/");
        _httpClient = new HttpClient { BaseAddress = new Uri(CurrentBaseUrl, UriKind.Absolute) };
        CurrentModeLabel = "API mode";
    }

    public string CurrentModeLabel { get; private set; }
    public string CurrentBaseUrl { get; private set; }

    public void ConfigureBaseUrl(string baseUrl)
    {
        CurrentBaseUrl = NormalizeBaseUrl(baseUrl);
        _httpClient.BaseAddress = new Uri(CurrentBaseUrl, UriKind.Absolute);
        _bearerToken = null; // reset token when URL changes
        _fallback.ConfigureBaseUrl(CurrentBaseUrl);
    }

    private async Task EnsureAuthenticatedAsync(CancellationToken cancellationToken)
    {
        if (_bearerToken != null) return;
        await _authLock.WaitAsync(cancellationToken);
        try
        {
            if (_bearerToken != null) return;
            var response = await _httpClient.PostAsJsonAsync(
                "api/auth/token",
                new { Password = "Admin@FleetMgr2026!" },
                cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
                _bearerToken = result.GetProperty("token").GetString();
                _httpClient.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", _bearerToken);
            }
        }
        catch { /* silently fail — API calls will 401 and fall back to offline mode */ }
        finally
        {
            _authLock.Release();
        }
    }

    public async Task<IReadOnlyList<NodeSummaryResponse>> GetNodesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await EnsureAuthenticatedAsync(cancellationToken);
            var response = await _httpClient.GetFromJsonAsync<List<NodeSummaryResponse>>("api/nodes", cancellationToken);
            CurrentModeLabel = "API mode";
            return response ?? new List<NodeSummaryResponse>();
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
        {
            CurrentModeLabel = _fallback.CurrentModeLabel;
            return await _fallback.GetNodesAsync(cancellationToken);
        }
    }

    public async Task<NodeSummaryResponse> CreateNodeAsync(CreateNodeRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            await EnsureAuthenticatedAsync(cancellationToken);
            var response = await _httpClient.PostAsJsonAsync("api/nodes", request, cancellationToken);
            response.EnsureSuccessStatusCode();
            CurrentModeLabel = "API mode";
            return await response.Content.ReadFromJsonAsync<NodeSummaryResponse>(cancellationToken: cancellationToken)
                ?? throw new InvalidOperationException("Node creation returned no payload.");
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
        {
            CurrentModeLabel = _fallback.CurrentModeLabel;
            return await _fallback.CreateNodeAsync(request, cancellationToken);
        }
    }

    public async Task<IReadOnlyList<AccountSummaryResponse>> GetAccountsAsync(Guid? nodeId = null, CancellationToken cancellationToken = default)
    {
        var path = nodeId.HasValue ? $"api/accounts?nodeId={nodeId.Value}" : "api/accounts";

        try
        {
            await EnsureAuthenticatedAsync(cancellationToken);
            var response = await _httpClient.GetFromJsonAsync<List<AccountSummaryResponse>>(path, cancellationToken);
            CurrentModeLabel = "API mode";
            return response ?? new List<AccountSummaryResponse>();
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
        {
            CurrentModeLabel = _fallback.CurrentModeLabel;
            return await _fallback.GetAccountsAsync(nodeId, cancellationToken);
        }
    }

    public async Task<AccountStageAlertDetailsResponse?> GetAccountStageAlertsAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        try
        {
            await EnsureAuthenticatedAsync(cancellationToken);
            CurrentModeLabel = "API mode";
            return await _httpClient.GetFromJsonAsync<AccountStageAlertDetailsResponse>($"api/accounts/{accountId}/stage-alerts", cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
        {
            CurrentModeLabel = _fallback.CurrentModeLabel;
            return await _fallback.GetAccountStageAlertsAsync(accountId, cancellationToken);
        }
    }

    public async Task<AccountSummaryResponse> CreateAccountAsync(CreateAccountRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            await EnsureAuthenticatedAsync(cancellationToken);
            var response = await _httpClient.PostAsJsonAsync("api/accounts", request, cancellationToken);
            response.EnsureSuccessStatusCode();
            CurrentModeLabel = "API mode";
            return await response.Content.ReadFromJsonAsync<AccountSummaryResponse>(cancellationToken: cancellationToken)
                ?? throw new InvalidOperationException("Account creation returned no payload.");
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
        {
            CurrentModeLabel = _fallback.CurrentModeLabel;
            return await _fallback.CreateAccountAsync(request, cancellationToken);
        }
    }

    public async Task<AccountSummaryResponse?> UpdateAccountAsync(Guid accountId, UpdateAccountRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            await EnsureAuthenticatedAsync(cancellationToken);
            var response = await _httpClient.PutAsJsonAsync($"api/accounts/{accountId}", request, cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                CurrentModeLabel = "API mode";
                return null;
            }

            response.EnsureSuccessStatusCode();
            CurrentModeLabel = "API mode";
            return await response.Content.ReadFromJsonAsync<AccountSummaryResponse>(cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
        {
            CurrentModeLabel = _fallback.CurrentModeLabel;
            return await _fallback.UpdateAccountAsync(accountId, request, cancellationToken);
        }
    }

    public async Task<bool> DeleteAccountAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        try
        {
            await EnsureAuthenticatedAsync(cancellationToken);
            using var response = await _httpClient.DeleteAsync($"api/accounts/{accountId}", cancellationToken);
            CurrentModeLabel = "API mode";
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
        {
            CurrentModeLabel = _fallback.CurrentModeLabel;
            return await _fallback.DeleteAccountAsync(accountId, cancellationToken);
        }
    }

    public async Task<Guid?> DispatchNodeCommandAsync(Guid nodeId, DispatchNodeCommandRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            await EnsureAuthenticatedAsync(cancellationToken);
            var response = await _httpClient.PostAsJsonAsync($"api/nodes/{nodeId}/commands", request, cancellationToken);
            response.EnsureSuccessStatusCode();
            CurrentModeLabel = "API mode";
            var payload = await response.Content.ReadFromJsonAsync<DispatchResponse>(cancellationToken: cancellationToken);
            return payload?.CommandId;
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
        {
            CurrentModeLabel = _fallback.CurrentModeLabel;
            return await _fallback.DispatchNodeCommandAsync(nodeId, request, cancellationToken);
        }
    }

    public async Task<NodeCommandStatusResponse?> GetNodeCommandStatusAsync(Guid nodeId, Guid commandId, CancellationToken cancellationToken = default)
    {
        try
        {
            await EnsureAuthenticatedAsync(cancellationToken);
            using var response = await _httpClient.GetAsync($"api/nodes/{nodeId}/commands/{commandId}", cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                CurrentModeLabel = "API mode";
                return null;
            }

            response.EnsureSuccessStatusCode();
            CurrentModeLabel = "API mode";
            return await response.Content.ReadFromJsonAsync<NodeCommandStatusResponse>(cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
        {
            CurrentModeLabel = _fallback.CurrentModeLabel;
            return await _fallback.GetNodeCommandStatusAsync(nodeId, commandId, cancellationToken);
        }
    }

    private static string NormalizeBaseUrl(string baseUrl)
        => baseUrl.EndsWith("/", StringComparison.Ordinal) ? baseUrl : $"{baseUrl}/";

    private sealed class DispatchResponse
    {
        public Guid CommandId { get; set; }
    }
}
