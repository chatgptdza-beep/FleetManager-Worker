using System.IO;
using System.Text.Json;
using FleetManager.Contracts.Accounts;
using FleetManager.Contracts.Nodes;
using FleetManager.Contracts.Operations;

namespace FleetManager.Desktop.Services;

public sealed class OfflineDashboardDataService : IDashboardDataService
{
    private readonly string _dbPath;
    private LocalDatabase _db = new();

    public OfflineDashboardDataService()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FleetManager");
        Directory.CreateDirectory(dir);
        _dbPath = Path.Combine(dir, "offline_db.json");
        LoadDb();
    }

    private void LoadDb()
    {
        if (File.Exists(_dbPath))
        {
            try
            {
                var json = File.ReadAllText(_dbPath);
                _db = JsonSerializer.Deserialize<LocalDatabase>(json) ?? new LocalDatabase();
            }
            catch { _db = new LocalDatabase(); }
        }
    }

    private void SaveDb()
    {
        var json = JsonSerializer.Serialize(_db, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_dbPath, json);
    }

    public string CurrentModeLabel => "Demo mode";
    public string CurrentBaseUrl { get; private set; } = "http://localhost:5188/";
    public string? BearerToken => null;

    public void ConfigureBaseUrl(string baseUrl)
    {
        CurrentBaseUrl = baseUrl.EndsWith("/", StringComparison.Ordinal) ? baseUrl : $"{baseUrl}/";
    }

    public Task<IReadOnlyList<NodeSummaryResponse>> GetNodesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<NodeSummaryResponse>>(_db.Nodes);

    public Task<NodeSummaryResponse?> GetNodeAsync(Guid nodeId, CancellationToken cancellationToken = default)
        => Task.FromResult<NodeSummaryResponse?>(_db.Nodes.FirstOrDefault(node => node.Id == nodeId));

    public Task<NodeSummaryResponse> CreateNodeAsync(CreateNodeRequest request, CancellationToken cancellationToken = default)
    {
        var node = new NodeSummaryResponse
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            IpAddress = request.IpAddress,
            SshPort = request.SshPort,
            ControlPort = request.ControlPort,
            SshUsername = request.SshUsername,
            AuthType = request.AuthType,
            OsType = request.OsType,
            Region = request.Region,
            Status = "Offline",
            ConnectionState = "Disconnected"
        };
        _db.Nodes.Add(node);
        SaveDb();
        return Task.FromResult(node);
    }

    public Task<IReadOnlyList<AccountSummaryResponse>> GetAccountsAsync(Guid? nodeId = null, CancellationToken cancellationToken = default)
    {
        var filtered = nodeId.HasValue 
            ? _db.Accounts.Where(a => a.NodeId == nodeId.Value).ToList() 
            : _db.Accounts;
        return Task.FromResult<IReadOnlyList<AccountSummaryResponse>>(filtered);
    }

    public Task<AccountSummaryResponse?> GetAccountAsync(Guid accountId, CancellationToken cancellationToken = default)
        => Task.FromResult<AccountSummaryResponse?>(_db.Accounts.FirstOrDefault(account => account.Id == accountId));

    public Task<AccountStageAlertDetailsResponse?> GetAccountStageAlertsAsync(Guid accountId, CancellationToken cancellationToken = default)
        => Task.FromResult<AccountStageAlertDetailsResponse?>(null);

    public Task<AccountSummaryResponse?> CompleteManualTakeoverAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        var account = _db.Accounts.FirstOrDefault(candidate => candidate.Id == accountId);
        if (account is null)
        {
            return Task.FromResult<AccountSummaryResponse?>(null);
        }

        var updated = new AccountSummaryResponse
        {
            Id = account.Id,
            NodeId = account.NodeId,
            Email = account.Email,
            Username = account.Username,
            Status = "Stable",
            CurrentStage = "Manual takeover complete",
            CurrentProxyIndex = account.CurrentProxyIndex,
            ProxyCount = account.ProxyCount
        };

        _db.Accounts[_db.Accounts.IndexOf(account)] = updated;
        SaveDb();
        return Task.FromResult<AccountSummaryResponse?>(updated);
    }

    public Task<InjectProxiesResponse> InjectProxiesAsync(Guid accountId, string rawProxies, bool replaceExisting = false, CancellationToken cancellationToken = default)
    {
        var account = _db.Accounts.FirstOrDefault(candidate => candidate.Id == accountId)
            ?? throw new InvalidOperationException("Account not found.");

        if (string.IsNullOrWhiteSpace(rawProxies))
        {
            throw new InvalidOperationException("Proxy list is empty.");
        }

        var injectedCount = rawProxies
            .Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Count(line => line.Contains(':', StringComparison.Ordinal));

        var clearedCount = replaceExisting ? account.ProxyCount : 0;
        if (replaceExisting)
        {
            account.ProxyCount = 0;
            account.CurrentProxyIndex = 0;
        }

        account.ProxyCount += injectedCount;
        SaveDb();

        return Task.FromResult(new InjectProxiesResponse
        {
            InjectedCount = injectedCount,
            TotalProxies = account.ProxyCount,
            ClearedCount = clearedCount
        });
    }

    public Task<RotateProxyResponse> RotateProxyAsync(Guid accountId, string? reason = null, CancellationToken cancellationToken = default)
    {
        var account = _db.Accounts.FirstOrDefault(candidate => candidate.Id == accountId)
            ?? throw new InvalidOperationException("Account not found.");

        if (account.ProxyCount <= 0)
        {
            throw new InvalidOperationException("No proxies available for rotation.");
        }

        account.CurrentProxyIndex = (account.CurrentProxyIndex + 1) % account.ProxyCount;
        SaveDb();

        return Task.FromResult(new RotateProxyResponse
        {
            NewIndex = account.CurrentProxyIndex
        });
    }

    public Task<IReadOnlyList<WorkerInboxEventResponse>> GetWorkerInboxEventsAsync(bool pendingOnly = true, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<WorkerInboxEventResponse>>(Array.Empty<WorkerInboxEventResponse>());

    public Task<bool> AcknowledgeWorkerInboxEventAsync(Guid eventId, CancellationToken cancellationToken = default)
        => Task.FromResult(false);

    public Task<AccountSummaryResponse> CreateAccountAsync(CreateAccountRequest request, CancellationToken cancellationToken = default)
    {
        var account = new AccountSummaryResponse
        {
            Id = Guid.NewGuid(),
            NodeId = request.NodeId,
            Email = request.Email,
            Username = request.Username,
            Status = request.Status ?? "Stopped",
            CurrentStage = "Idle",
            CurrentProxyIndex = 0,
            ProxyCount = 0
        };
        _db.Accounts.Add(account);
        SaveDb();
        return Task.FromResult(account);
    }

    public Task<AccountSummaryResponse?> UpdateAccountAsync(Guid accountId, UpdateAccountRequest request, CancellationToken cancellationToken = default)
    {
        var account = _db.Accounts.FirstOrDefault(a => a.Id == accountId);
        if (account != null)
        {
            var updated = new AccountSummaryResponse
            {
                Id = account.Id,
                NodeId = account.NodeId,
                Email = request.Email ?? account.Email,
                Username = request.Username ?? account.Username,
                Status = request.Status ?? account.Status,
                CurrentStage = account.CurrentStage,
                CurrentProxyIndex = account.CurrentProxyIndex,
                ProxyCount = account.ProxyCount
            };
            _db.Accounts[_db.Accounts.IndexOf(account)] = updated;
            SaveDb();
            return Task.FromResult<AccountSummaryResponse?>(updated);
        }
        return Task.FromResult<AccountSummaryResponse?>(null);
    }

    public Task<bool> DeleteAccountAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        var removed = _db.Accounts.RemoveAll(a => a.Id == accountId) > 0;
        if (removed) SaveDb();
        return Task.FromResult(removed);
    }

    public Task<bool> DeleteNodeAsync(Guid nodeId, CancellationToken cancellationToken = default)
    {
        var removed = _db.Nodes.RemoveAll(n => n.Id == nodeId) > 0;
        if (removed) SaveDb();
        return Task.FromResult(removed);
    }

    public Task<Guid?> DispatchNodeCommandAsync(Guid nodeId, DispatchNodeCommandRequest request, CancellationToken cancellationToken = default)
    {
        // Mock command execution offline
        return Task.FromResult<Guid?>(Guid.NewGuid());
    }

    public Task<NodeCommandStatusResponse?> GetNodeCommandStatusAsync(Guid nodeId, Guid commandId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<NodeCommandStatusResponse?>(new NodeCommandStatusResponse
        {
            CommandId = commandId,
            NodeId = nodeId,
            Status = "Executed",
            ResultMessage = "Demo mode dummy success"
        });
    }

    private class LocalDatabase
    {
        public List<NodeSummaryResponse> Nodes { get; set; } = new();
        public List<AccountSummaryResponse> Accounts { get; set; } = new();
    }
}
