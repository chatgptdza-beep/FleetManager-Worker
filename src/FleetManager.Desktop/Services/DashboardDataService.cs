using System.Net.Http;
using System.Net.Http.Json;
using FleetManager.Contracts.Accounts;
using FleetManager.Contracts.Nodes;

namespace FleetManager.Desktop.Services;

public sealed class DashboardDataService : IDashboardDataService
{
    private readonly HttpClient _httpClient;
    private readonly OfflineDashboardDataService _fallback = new();

    public DashboardDataService()
    {
        CurrentBaseUrl = NormalizeBaseUrl(Environment.GetEnvironmentVariable("FLEETMANAGER_API_BASE_URL") ?? "http://localhost:5188/");
        _httpClient = new HttpClient { BaseAddress = new Uri(CurrentBaseUrl, UriKind.Absolute) };
        CurrentModeLabel = "API mode";
    }

    public string CurrentModeLabel { get; private set; }
    public string CurrentBaseUrl { get; private set; }

    public void ConfigureBaseUrl(string baseUrl)
    {
        CurrentBaseUrl = NormalizeBaseUrl(baseUrl);
        _httpClient.BaseAddress = new Uri(CurrentBaseUrl, UriKind.Absolute);
        _fallback.ConfigureBaseUrl(CurrentBaseUrl);
    }

    public async Task<IReadOnlyList<NodeSummaryResponse>> GetNodesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
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
