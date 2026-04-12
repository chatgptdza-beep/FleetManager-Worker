using FleetManager.Contracts.Accounts;

namespace FleetManager.Application.Abstractions;

public interface IAccountService
{
    Task<IReadOnlyList<AccountSummaryResponse>> GetAccountsAsync(Guid? nodeId = null, CancellationToken cancellationToken = default);
    Task<AccountSummaryResponse?> GetAccountAsync(Guid accountId, CancellationToken cancellationToken = default);
    Task<AccountStageAlertDetailsResponse?> GetAccountStageAlertsAsync(Guid accountId, CancellationToken cancellationToken = default);
    Task<AccountSummaryResponse> CreateAccountAsync(CreateAccountRequest request, CancellationToken cancellationToken = default);
    Task<AccountSummaryResponse?> UpdateAccountAsync(Guid accountId, UpdateAccountRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteAccountAsync(Guid accountId, CancellationToken cancellationToken = default);
    Task<AccountSummaryResponse?> SetAccountStatusManualAsync(Guid accountId, CancellationToken cancellationToken = default);
}
