using FleetManager.Contracts.Accounts;
using FleetManager.Contracts.Nodes;

namespace FleetManager.Desktop.Services;

public interface IDashboardDataService
{
    string CurrentModeLabel { get; }
    string CurrentBaseUrl { get; }
    string? BearerToken { get; }
    void ConfigureBaseUrl(string baseUrl);
    Task<IReadOnlyList<NodeSummaryResponse>> GetNodesAsync(CancellationToken cancellationToken = default);
    Task<NodeSummaryResponse> CreateNodeAsync(CreateNodeRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AccountSummaryResponse>> GetAccountsAsync(Guid? nodeId = null, CancellationToken cancellationToken = default);
    Task<AccountStageAlertDetailsResponse?> GetAccountStageAlertsAsync(Guid accountId, CancellationToken cancellationToken = default);
    Task<AccountSummaryResponse> CreateAccountAsync(CreateAccountRequest request, CancellationToken cancellationToken = default);
    Task<AccountSummaryResponse?> UpdateAccountAsync(Guid accountId, UpdateAccountRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteAccountAsync(Guid accountId, CancellationToken cancellationToken = default);
    Task<Guid?> DispatchNodeCommandAsync(Guid nodeId, DispatchNodeCommandRequest request, CancellationToken cancellationToken = default);
    Task<NodeCommandStatusResponse?> GetNodeCommandStatusAsync(Guid nodeId, Guid commandId, CancellationToken cancellationToken = default);
}
