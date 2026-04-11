using FleetManager.Contracts.Accounts;
using FleetManager.Contracts.Nodes;

namespace FleetManager.Desktop.Services;

public sealed class OfflineDashboardDataService : IDashboardDataService
{
    public string CurrentModeLabel => "Offline mode";
    public string CurrentBaseUrl { get; private set; } = "http://localhost:5188/";
    public string? BearerToken => null;

    public void ConfigureBaseUrl(string baseUrl)
    {
        CurrentBaseUrl = baseUrl.EndsWith("/", StringComparison.Ordinal) ? baseUrl : $"{baseUrl}/";
    }

    public Task<IReadOnlyList<NodeSummaryResponse>> GetNodesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<NodeSummaryResponse>>(new List<NodeSummaryResponse>());

    public Task<NodeSummaryResponse> CreateNodeAsync(CreateNodeRequest request, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Cannot create nodes in offline mode. Please start the API server.");

    public Task<IReadOnlyList<AccountSummaryResponse>> GetAccountsAsync(Guid? nodeId = null, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<AccountSummaryResponse>>(new List<AccountSummaryResponse>());

    public Task<AccountStageAlertDetailsResponse?> GetAccountStageAlertsAsync(Guid accountId, CancellationToken cancellationToken = default)
        => Task.FromResult<AccountStageAlertDetailsResponse?>(null);

    public Task<AccountSummaryResponse> CreateAccountAsync(CreateAccountRequest request, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Cannot create accounts in offline mode. Please start the API server.");

    public Task<AccountSummaryResponse?> UpdateAccountAsync(Guid accountId, UpdateAccountRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult<AccountSummaryResponse?>(null);

    public Task<bool> DeleteAccountAsync(Guid accountId, CancellationToken cancellationToken = default)
        => Task.FromResult(false);

    public Task<Guid?> DispatchNodeCommandAsync(Guid nodeId, DispatchNodeCommandRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult<Guid?>(null);

    public Task<NodeCommandStatusResponse?> GetNodeCommandStatusAsync(Guid nodeId, Guid commandId, CancellationToken cancellationToken = default)
        => Task.FromResult<NodeCommandStatusResponse?>(null);
}
