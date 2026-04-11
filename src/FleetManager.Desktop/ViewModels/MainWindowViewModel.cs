using System.Collections.ObjectModel;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using FleetManager.Contracts.Accounts;
using FleetManager.Contracts.Nodes;
using FleetManager.Desktop.Models;
using FleetManager.Desktop.Services;

namespace FleetManager.Desktop.ViewModels;

public enum MainView
{
    Dashboard,
    Accounts,
    Nodes,
    ManualQueue,
    Settings
}

public sealed class MainWindowViewModel : ViewModelBase
{
    private MainView _currentView = MainView.Dashboard;
    private readonly IDashboardDataService _dataService;
    private readonly List<AccountCardViewModel> _allAccounts = new();
    private readonly List<AccountCardViewModel> _selectedNodeAccounts = new();
    private readonly Dictionary<Guid, ViewerSessionInfo> _viewerSessions = new();
    private readonly SignalRService _signalR = new();
    private string _signalRStatus = "Not connected";
    private NodeCardViewModel? _selectedNode;
    private AccountCardViewModel? _selectedAccount;
    private AccountCardViewModel? _focusedAccount;
    private string _searchText = string.Empty;
    private string _statusMessage = "Loading dashboard...";
    private string _focusedViewerUrl = string.Empty;
    private string _focusedViewerMessage = "Request a viewer session to see the viewer URL here.";
    private bool _isInitializing;

    public ObservableCollection<NodeCardViewModel> Nodes { get; } = new();
    public ObservableCollection<AccountCardViewModel> Accounts { get; } = new();
    public ObservableCollection<AccountCardViewModel> ManualQueueAccounts { get; } = new();

    public MainView CurrentView
    {
        get => _currentView;
        set
        {
            if (SetProperty(ref _currentView, value))
            {
                NotifyDashboardPropertiesChanged();
            }
        }
    }

    public Visibility DashboardViewVisibility => CurrentView == MainView.Dashboard ? Visibility.Visible : Visibility.Collapsed;
    public Visibility AccountsViewVisibility => CurrentView == MainView.Accounts ? Visibility.Visible : Visibility.Collapsed;
    public Visibility NodesViewVisibility => CurrentView == MainView.Nodes ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ManualQueueViewVisibility => CurrentView == MainView.ManualQueue ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SettingsViewVisibility => CurrentView == MainView.Settings ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ManualQueueEmptyVisibility => ManualQueueAccounts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public bool IsDashboardActive => CurrentView == MainView.Dashboard;
    public bool IsAccountsActive => CurrentView == MainView.Accounts;
    public bool IsNodesActive => CurrentView == MainView.Nodes;
    public bool IsManualQueueActive => CurrentView == MainView.ManualQueue;
    public bool IsSettingsActive => CurrentView == MainView.Settings;

    public void SwitchView(string viewName)
    {
        if (Enum.TryParse<MainView>(viewName, true, out var view))
        {
            CurrentView = view;
        }
    }

    public NodeCardViewModel? SelectedNode
    {
        get => _selectedNode;
        set
        {
            if (SetProperty(ref _selectedNode, value) && !_isInitializing)
            {
                _ = LoadAccountsForSelectedNodeAsync();
                NotifyDashboardPropertiesChanged();
            }
        }
    }

    public AccountCardViewModel? SelectedAccount
    {
        get => _selectedAccount;
        set
        {
            if (SetProperty(ref _selectedAccount, value))
            {
                if (value is null)
                {
                    FocusedAccount = null;
                }
                else
                {
                    _ = LoadSelectedAccountDetailsAsync(value.AccountId);
                }

                NotifySelectionChanged();
            }
        }
    }

    public AccountCardViewModel? FocusedAccount
    {
        get => _focusedAccount;
        private set
        {
            if (SetProperty(ref _focusedAccount, value))
            {
                OnPropertyChanged(nameof(FocusedAccountName));
                OnPropertyChanged(nameof(FocusedAccountSummary));
                OnPropertyChanged(nameof(FocusedAccountPlaceholderVisibility));
                OnPropertyChanged(nameof(FocusedAccountDetailVisibility));
                OnPropertyChanged(nameof(FocusedAccountAlertVisibility));
                OnPropertyChanged(nameof(FocusedAccountStages));
                RefreshFocusedViewerState();
            }
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                ApplyVisibleAccountsFilter();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public int NodeCount => Nodes.Count;
    public int TotalAccountCount => _allAccounts.Count;
    public int VisibleAccountCount => Accounts.Count;
    public int ManualRequiredCount => _allAccounts.Count(IsManualQueueCandidate);
    public int TotalActiveSessions => Nodes.Sum(node => node.ActiveSessions);
    public string NodeThroughputLabel => NodeCount == 0
        ? "0x0"
        : $"{Math.Max(1, (int)Math.Ceiling((double)TotalAccountCount / NodeCount))}x{NodeCount}";

    public string AveragePingLabel => $"{(Nodes.Count == 0 ? 0 : (int)Math.Round(Nodes.Average(node => node.PingMs)))} ms";
    public string ConnectedNodesSummary => NodeCount == 0
        ? "0 / 0"
        : $"{Nodes.Count(node => node.IsConnected)} / {NodeCount}";

    public string ProfilesLoadedLabel => TotalAccountCount.ToString("00");
    public string ManualQueueLabel => ManualRequiredCount.ToString("00");
    public string AccountsGroupLabel => $"{NodeCount} groups";
    public string SourceBanner => $"Source: {_dataService.CurrentModeLabel} | API: {_dataService.CurrentBaseUrl}";
    public string AccountsPanelTitle => "Accounts By VPS";
    public string SelectedNodeTableSummary => SelectedNode is null
        ? "Current view: select a VPS tab to inspect assigned accounts."
        : $"Current view: all accounts running on {SelectedNode.Name} appear in the table below.";
    public string SelectedNodePanelTitle => SelectedNode is null ? "Selected VPS" : $"{SelectedNode.Name} bootstrap";
    public string SelectedNodePanelSummary => SelectedNode is null
        ? "Select a VPS tab to inspect worker setup and copy-ready appsettings."
        : $"{SelectedNode.OsType} | {SelectedNode.RegionLabel} | SSH {SelectedNode.SshLabel}";
    public string SelectedNodeIdentityLabel => SelectedNode is null ? "--" : SelectedNode.NodeId.ToString();
    public string SelectedNodeRuntimeLabel => SelectedNode is null
        ? "Worker runtime not available."
        : $"Agent {SelectedNode.AgentVersionLabel} | Control {SelectedNode.ControlPort} | {SelectedNode.HeartbeatLabel}";
    public string SelectedNodeInstallCommand => SelectedNode is null
        ? string.Empty
        : BuildInstallCommand(SelectedNode, _dataService.CurrentBaseUrl);
    public string SelectedNodeWorkerConfigJson
    {
        get => SelectedNode is null
            ? string.Empty
            : BuildWorkerConfigJson(SelectedNode);
        set { }
    }
    public Visibility SelectedNodeDetailVisibility => SelectedNode is null ? Visibility.Collapsed : Visibility.Visible;
    public Visibility SelectedNodePlaceholderVisibility => SelectedNode is null ? Visibility.Visible : Visibility.Collapsed;

    public string TableHint => "Manual rows stay highlighted, support right-click actions, and can be targeted from the bulk action bar.";
    public string FocusedAccountName => FocusedAccount?.DisplayName ?? "No account selected";
    public string FocusedAccountSummary => FocusedAccount?.DetailSummary ?? "Select an account to inspect its workflow timeline and current alert state.";
    public Visibility FocusedAccountPlaceholderVisibility => FocusedAccount is null ? Visibility.Visible : Visibility.Collapsed;
    public Visibility FocusedAccountDetailVisibility => FocusedAccount is null ? Visibility.Collapsed : Visibility.Visible;
    public Visibility FocusedAccountAlertVisibility => FocusedAccount is { HasIssue: true } ? Visibility.Visible : Visibility.Collapsed;
    public IReadOnlyList<AccountStageViewModel> FocusedAccountStages => FocusedAccount is null
        ? Array.Empty<AccountStageViewModel>()
        : FocusedAccount.Stages.ToList();
    public string FocusedViewerUrl => _focusedViewerUrl;
    public string FocusedViewerMessage => _focusedViewerMessage;
    public Visibility FocusedViewerPanelVisibility => FocusedAccount is null ? Visibility.Collapsed : Visibility.Visible;
    public Visibility FocusedViewerUrlVisibility => string.IsNullOrWhiteSpace(FocusedViewerUrl) ? Visibility.Collapsed : Visibility.Visible;
    public int CheckedAccountCount => Accounts.Count(account => account.IsChecked);
    public int ActionTargetCount => GetActionTargets().Count;
    public string SelectionSummary => ActionTargetCount == 0 ? "Targets: none" : $"Targets: {ActionTargetCount} account(s)";
    public string BulkActionHint => CheckedAccountCount > 0
        ? "Bulk mode is active. Commands target all checked rows."
        : SelectedAccount is null
            ? "Select one account or check multiple rows to use the action bar."
            : $"Single target: {SelectedAccount.Email}";

    public MainWindowViewModel()
        : this(new DashboardDataService())
    {
    }

    public MainWindowViewModel(IDashboardDataService dataService)
    {
        _dataService = dataService;
    }

    /// <summary>Exposes the data service for use in code-behind (e.g. RemoteTakeoverWindow).</summary>
    public IDashboardDataService DataService => _dataService;

    public async Task InitializeAsync()
    {
        await ReloadAsync();
        await TryConnectSignalRAsync();
    }

    public async Task ReloadAsync(Guid? preferredNodeId = null, Guid? preferredAccountId = null)
    {
        _isInitializing = true;
        try
        {
            await LoadNodesAsync();
            await LoadAllAccountsAsync();

            SelectedNode = ResolveSelectedNode(preferredNodeId);
            await LoadAccountsForSelectedNodeAsync(preferredAccountId);
            StatusMessage = $"{_dataService.CurrentModeLabel} | {Nodes.Count} nodes, {TotalAccountCount} accounts | {_dataService.CurrentBaseUrl}";
        }
        finally
        {
            _isInitializing = false;
            NotifyDashboardPropertiesChanged();
        }
    }

    private async Task TryConnectSignalRAsync()
    {
        try
        {
            var token = _dataService.BearerToken;
            if (string.IsNullOrWhiteSpace(token))
            {
                SignalRStatusLabel = "No auth token";
                return;
            }

            _signalR.OnBotStatusChanged += OnSignalRBotStatusChanged;
            _signalR.OnManualRequired += OnSignalRManualRequired;
            _signalR.OnProxyRotated += OnSignalRProxyRotated;

            await _signalR.ConnectAsync(_dataService.CurrentBaseUrl, token);
            SignalRStatusLabel = _signalR.IsConnected ? "Connected" : "Connection failed";
        }
        catch
        {
            SignalRStatusLabel = "Connection failed";
        }
    }

    public async Task DisconnectSignalRAsync()
    {
        try
        {
            _signalR.OnBotStatusChanged -= OnSignalRBotStatusChanged;
            _signalR.OnManualRequired -= OnSignalRManualRequired;
            _signalR.OnProxyRotated -= OnSignalRProxyRotated;
            await _signalR.DisconnectAsync();
            SignalRStatusLabel = "Disconnected";
        }
        catch { /* cleanup best-effort */ }
    }

    private void OnSignalRBotStatusChanged(Guid accountId, string status)
    {
        Application.Current?.Dispatcher?.InvokeAsync(async () =>
        {
            await ReloadAsync(SelectedNode?.NodeId, accountId);
            StatusMessage = $"⚡ Live: {status}";
        });
    }

    private void OnSignalRManualRequired(Guid accountId, string vncUrl)
    {
        Application.Current?.Dispatcher?.InvokeAsync(async () =>
        {
            _viewerSessions[accountId] = new ViewerSessionInfo(vncUrl, $"Manual takeover ready: {vncUrl}");
            await ReloadAsync(SelectedNode?.NodeId, accountId);
            StatusMessage = $"⚡ Live: Manual required for account";
        });
    }

    private void OnSignalRProxyRotated(Guid accountId, int newIndex)
    {
        Application.Current?.Dispatcher?.InvokeAsync(async () =>
        {
            await ReloadAsync(SelectedNode?.NodeId, accountId);
            StatusMessage = $"⚡ Live: Proxy rotated (index {newIndex})";
        });
    }

    public async Task DeleteNodeAsync(Guid nodeId)
    {
        var deleted = await _dataService.DeleteNodeAsync(nodeId);
        if (deleted)
        {
            await ReloadAsync();
            StatusMessage = "VPS node deleted successfully";
        }
        else
        {
            StatusMessage = "Failed to delete VPS node";
        }
    }

    public async Task CreateAccountAsync(CreateAccountRequest request)
    {
        var created = await _dataService.CreateAccountAsync(request);
        await ReloadAsync(request.NodeId, created.Id);
        StatusMessage = $"Created {created.Email} | {SourceBanner}";
    }

    public async Task CreateNodeAsync(CreateNodeRequest request)
    {
        var created = await _dataService.CreateNodeAsync(request);
        await ReloadAsync(created.Id);
        StatusMessage = $"Added {created.Name} | NodeId: {created.Id} | {SourceBanner}";
    }

    public async Task UpdateAccountAsync(Guid accountId, UpdateAccountRequest request)
    {
        var updated = await _dataService.UpdateAccountAsync(accountId, request);
        if (updated is null)
        {
            throw new InvalidOperationException("Account not found.");
        }

        await ReloadAsync(updated.NodeId, updated.Id);
        StatusMessage = $"Updated {updated.Email} | {SourceBanner}";
    }

    public async Task DeleteAccountAsync(Guid accountId)
    {
        var deleted = await _dataService.DeleteAccountAsync(accountId);
        if (!deleted)
        {
            throw new InvalidOperationException("Account not found.");
        }

        await ReloadAsync(SelectedNode?.NodeId);
        StatusMessage = $"Deleted account {accountId} | {SourceBanner}";
    }

    public Task ExecutePrimaryActionAsync(AccountCardViewModel account)
        => account.PrimaryCommandType == "OpenAssignedSession"
            ? OpenRemoteViewerAsync(account)
            : DispatchAccountCommandAsync(account, account.PrimaryCommandType);

    public Task StartAccountAsync(AccountCardViewModel account)
        => DispatchAccountCommandAsync(account, "StartBrowser");

    public Task StopAccountAsync(AccountCardViewModel account)
        => DispatchAccountCommandAsync(account, "StopBrowser");

    public async Task OpenRemoteViewerAsync(AccountCardViewModel account)
    {
        var commandId = await _dataService.DispatchNodeCommandAsync(account.NodeId, new DispatchNodeCommandRequest
        {
            CommandType = "OpenAssignedSession",
            PayloadJson = BuildAccountPayload(account)
        });

        if (!commandId.HasValue)
        {
            throw new InvalidOperationException("Viewer command dispatch failed.");
        }

        var command = await WaitForCommandResultAsync(account.NodeId, commandId.Value);
        var viewerUrl = NormalizeViewerUrl(ExtractViewerUrl(command?.ResultMessage), account);
        var viewerMessage = command?.ResultMessage ?? "Viewer command queued. Wait for the worker to publish the session URL.";

        _viewerSessions[account.AccountId] = new ViewerSessionInfo(viewerUrl, viewerMessage);

        await ReloadAsync(SelectedNode?.NodeId, account.AccountId);
        StatusMessage = string.IsNullOrWhiteSpace(viewerUrl)
            ? $"Viewer queued for {account.Email} | {SourceBanner}"
            : $"Viewer ready for {account.Email}: {viewerUrl} | {SourceBanner}";
    }

    public Task BringToFrontAsync(AccountCardViewModel account)
        => DispatchAccountCommandAsync(account, "BringManagedWindowToFront");

    public Task OpenLogsAsync(AccountCardViewModel account)
        => DispatchAccountCommandAsync(account, "FetchSessionLogs");

    public Task StartSelectedAccountsAsync()
        => DispatchBulkCommandsAsync("StartBrowser", "Start");

    public Task StopSelectedAccountsAsync()
        => DispatchBulkCommandsAsync("StopBrowser", "Stop");

    public Task LoginSelectedAccountsAsync()
        => DispatchBulkCommandsAsync("LoginWorkflow", "Login");

    public Task StartAutomationSelectedAccountsAsync()
        => DispatchBulkCommandsAsync("StartAutomation", "Start auto");

    public Task StopAutomationSelectedAccountsAsync()
        => DispatchBulkCommandsAsync("StopAutomation", "Stop auto");

    public Task PauseAutomationSelectedAccountsAsync()
        => DispatchBulkCommandsAsync("PauseAutomation", "Pause auto");

    public async Task DeleteSelectedAccountsAsync()
    {
        var targets = GetActionTargets();
        if (targets.Count == 0)
        {
            throw new InvalidOperationException("Select one account or check multiple rows first.");
        }

        foreach (var account in targets)
        {
            var deleted = await _dataService.DeleteAccountAsync(account.AccountId);
            if (!deleted)
            {
                throw new InvalidOperationException($"Failed to delete {account.Email}.");
            }
        }

        await ReloadAsync(SelectedNode?.NodeId);
        StatusMessage = $"Deleted {targets.Count} account(s) | {SourceBanner}";
    }

    public void SelectAllVisibleAccounts()
    {
        foreach (var account in Accounts)
        {
            account.IsChecked = true;
        }

        NotifySelectionChanged();
    }

    public void ClearActionTargets()
    {
        foreach (var account in Accounts)
        {
            account.IsChecked = false;
        }

        SelectedAccount = null;
        FocusedAccount = null;
        NotifySelectionChanged();
    }

    public void NotifySelectionChanged()
    {
        OnPropertyChanged(nameof(CheckedAccountCount));
        OnPropertyChanged(nameof(ActionTargetCount));
        OnPropertyChanged(nameof(SelectionSummary));
        OnPropertyChanged(nameof(BulkActionHint));
    }

    public async Task SaveDesktopConfigAsync(string path)
    {
        var snapshot = new DesktopConfigSnapshot
        {
            ApiBaseUrl = _dataService.CurrentBaseUrl,
            SelectedNodeId = SelectedNode?.NodeId,
            SearchText = SearchText
        };

        var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(path, json);
        StatusMessage = $"Saved desktop config to {Path.GetFileName(path)} | {SourceBanner}";
    }

    public async Task LoadDesktopConfigAsync(string path)
    {
        var json = await File.ReadAllTextAsync(path);
        var snapshot = JsonSerializer.Deserialize<DesktopConfigSnapshot>(json)
            ?? throw new InvalidOperationException("Desktop config is invalid.");

        _dataService.ConfigureBaseUrl(snapshot.ApiBaseUrl);
        SearchText = snapshot.SearchText ?? string.Empty;
        await ReloadAsync(snapshot.SelectedNodeId);
        StatusMessage = $"Loaded desktop config from {Path.GetFileName(path)} | {SourceBanner}";
    }

    public async Task ExportVisibleAccountsCsvAsync(string path)
    {
        var builder = new StringBuilder();
        builder.AppendLine("AccountId,Email,Username,NodeName,NodeIpAddress,Status,CurrentStage,AlertTitle");

        foreach (var account in Accounts)
        {
            builder.AppendLine(string.Join(",",
                EscapeCsv(account.AccountId.ToString()),
                EscapeCsv(account.Email),
                EscapeCsv(account.Username),
                EscapeCsv(account.NodeName),
                EscapeCsv(account.NodeIpAddress),
                EscapeCsv(account.Status),
                EscapeCsv(account.CurrentStage),
                EscapeCsv(account.ActiveAlertTitle ?? string.Empty)));
        }

        await File.WriteAllTextAsync(path, builder.ToString());
        StatusMessage = $"Exported {Accounts.Count} visible rows to {Path.GetFileName(path)} | {SourceBanner}";
    }

    private async Task DispatchAccountCommandAsync(AccountCardViewModel account, string commandType)
    {
        var commandId = await _dataService.DispatchNodeCommandAsync(account.NodeId, new DispatchNodeCommandRequest
        {
            CommandType = commandType,
            PayloadJson = BuildAccountPayload(account)
        });

        if (!commandId.HasValue)
        {
            throw new InvalidOperationException("Command dispatch failed.");
        }

        if (ShouldWaitForCommandCompletion(commandType))
        {
            var command = await WaitForCommandResultAsync(account.NodeId, commandId.Value);
            if (command is null)
            {
                throw new InvalidOperationException($"{commandType} did not return a status update.");
            }

            if (command.Status is "Failed" or "TimedOut")
            {
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(command.ResultMessage)
                    ? $"{commandType} failed for {account.Email}."
                    : command.ResultMessage);
            }

            // Automatically update the account status if it's a lifecycle command
            if (commandType == "StartBrowser")
            {
                await _dataService.UpdateAccountAsync(account.AccountId, new UpdateAccountRequest { Status = "Running", Email = account.Email, Username = account.Username });
            }
            else if (commandType == "StopBrowser")
            {
                await _dataService.UpdateAccountAsync(account.AccountId, new UpdateAccountRequest { Status = "Stopped", Email = account.Email, Username = account.Username });
            }

            await ReloadAsync(SelectedNode?.NodeId, account.AccountId);
            StatusMessage = $"{commandType} executed for {account.Email} | {SourceBanner}";
            return;
        }

        // For queued commands, we might want to enthusiastically update status too
        if (commandType == "StartBrowser")
        {
            await _dataService.UpdateAccountAsync(account.AccountId, new UpdateAccountRequest { Status = "Waiting", Email = account.Email, Username = account.Username });
        }
        else if (commandType == "StopBrowser")
        {
            await _dataService.UpdateAccountAsync(account.AccountId, new UpdateAccountRequest { Status = "Stopping", Email = account.Email, Username = account.Username });
        }

        await ReloadAsync(SelectedNode?.NodeId, account.AccountId);
        StatusMessage = $"Queued {commandType} for {account.Email} | {SourceBanner}";
    }

    private async Task DispatchBulkCommandsAsync(string commandType, string actionLabel)
    {
        var targets = GetActionTargets();
        if (targets.Count == 0)
        {
            throw new InvalidOperationException("Select one account or check multiple rows first.");
        }

        var successCount = 0;
        var failedAccounts = new List<string>();

        foreach (var account in targets)
        {
            var commandId = await _dataService.DispatchNodeCommandAsync(account.NodeId, new DispatchNodeCommandRequest
            {
                CommandType = commandType,
                PayloadJson = BuildAccountPayload(account)
            });

            if (!commandId.HasValue)
            {
                throw new InvalidOperationException($"Command dispatch failed for {account.Email}.");
            }

            if (ShouldWaitForCommandCompletion(commandType))
            {
                var command = await WaitForCommandResultAsync(account.NodeId, commandId.Value);
                if (command is null || command.Status is "Failed" or "TimedOut")
                {
                    failedAccounts.Add(account.Email);
                    continue;
                }

                if (commandType == "StartBrowser")
                {
                    await _dataService.UpdateAccountAsync(account.AccountId, new UpdateAccountRequest { Status = "Running", Email = account.Email, Username = account.Username });
                }
                else if (commandType == "StopBrowser")
                {
                    await _dataService.UpdateAccountAsync(account.AccountId, new UpdateAccountRequest { Status = "Stopped", Email = account.Email, Username = account.Username });
                }
            }
            else
            {
                if (commandType == "StartBrowser")
                {
                    await _dataService.UpdateAccountAsync(account.AccountId, new UpdateAccountRequest { Status = "Waiting", Email = account.Email, Username = account.Username });
                }
                else if (commandType == "StopBrowser")
                {
                    await _dataService.UpdateAccountAsync(account.AccountId, new UpdateAccountRequest { Status = "Stopping", Email = account.Email, Username = account.Username });
                }
            }

            successCount++;
        }

        await ReloadAsync(SelectedNode?.NodeId, targets.Count == 1 ? targets[0].AccountId : null);

        if (failedAccounts.Count > 0)
        {
            var failedPreview = string.Join(", ", failedAccounts.Take(3));
            var suffix = failedAccounts.Count > 3 ? "..." : string.Empty;
            throw new InvalidOperationException($"{actionLabel}: {successCount} succeeded, {failedAccounts.Count} failed ({failedPreview}{suffix}).");
        }

        StatusMessage = ShouldWaitForCommandCompletion(commandType)
            ? $"{actionLabel} executed for {successCount} account(s) | {SourceBanner}"
            : $"{actionLabel} queued for {targets.Count} account(s) | {SourceBanner}";
    }

    private static bool ShouldWaitForCommandCompletion(string commandType)
        => string.Equals(commandType, "StartBrowser", StringComparison.Ordinal)
           || string.Equals(commandType, "StopBrowser", StringComparison.Ordinal)
           || string.Equals(commandType, "OpenAssignedSession", StringComparison.Ordinal);

    private async Task LoadNodesAsync()
    {
        var nodes = await _dataService.GetNodesAsync();
        Nodes.Clear();
        foreach (var node in nodes.OrderBy(node => node.Name).Select(NodeCardViewModel.FromContract))
        {
            Nodes.Add(node);
        }

        NotifyDashboardPropertiesChanged();
    }

    private async Task LoadAllAccountsAsync()
    {
        var accounts = await _dataService.GetAccountsAsync();
        _allAccounts.Clear();
        _allAccounts.AddRange(accounts.Select(AccountCardViewModel.FromSummary).OrderBy(account => account.Email));
        RefreshManualQueue();
        NotifyDashboardPropertiesChanged();
    }

    private async Task LoadAccountsForSelectedNodeAsync(Guid? preferredAccountId = null)
    {
        _selectedNodeAccounts.Clear();

        if (SelectedNode is not null)
        {
            var accounts = await _dataService.GetAccountsAsync(SelectedNode.NodeId);
            _selectedNodeAccounts.AddRange(accounts.Select(AccountCardViewModel.FromSummary).OrderBy(account => account.Email));
        }

        ApplyVisibleAccountsFilter(preferredAccountId);
        StatusMessage = SourceBanner;
    }

    private async Task LoadSelectedAccountDetailsAsync(Guid accountId)
    {
        var details = await _dataService.GetAccountStageAlertsAsync(accountId);
        FocusedAccount = details is null ? SelectedAccount : AccountCardViewModel.FromDetails(details);
        StatusMessage = SourceBanner;
    }

    private void ApplyVisibleAccountsFilter(Guid? preferredAccountId = null)
    {
        var filteredAccounts = _selectedNodeAccounts
            .Where(account => MatchesSearch(account, SearchText))
            .ToList();

        Accounts.Clear();
        foreach (var account in filteredAccounts)
        {
            Accounts.Add(account);
        }

        var nextSelected = ResolveSelectedAccount(preferredAccountId);
        SelectedAccount = nextSelected;
        if (nextSelected is null)
        {
            FocusedAccount = null;
        }

        NotifyDashboardPropertiesChanged();
        NotifySelectionChanged();
    }

    private void RefreshManualQueue()
    {
        ManualQueueAccounts.Clear();
        foreach (var account in _allAccounts
                     .Where(IsManualQueueCandidate)
                     .OrderByDescending(account => account.RequiresManualAttention)
                     .ThenBy(account => account.NodeName)
                     .Take(6))
        {
            ManualQueueAccounts.Add(account);
        }
    }

    private NodeCardViewModel? ResolveSelectedNode(Guid? preferredNodeId)
    {
        if (preferredNodeId.HasValue)
        {
            return Nodes.FirstOrDefault(node => node.NodeId == preferredNodeId.Value) ?? Nodes.FirstOrDefault();
        }

        if (SelectedNode is not null)
        {
            return Nodes.FirstOrDefault(node => node.NodeId == SelectedNode.NodeId) ?? Nodes.FirstOrDefault();
        }

        return Nodes.FirstOrDefault();
    }

    private AccountCardViewModel? ResolveSelectedAccount(Guid? preferredAccountId)
    {
        if (preferredAccountId.HasValue)
        {
            return Accounts.FirstOrDefault(account => account.AccountId == preferredAccountId.Value);
        }

        if (SelectedAccount is not null)
        {
            return Accounts.FirstOrDefault(account => account.AccountId == SelectedAccount.AccountId) ?? Accounts.FirstOrDefault();
        }

        return Accounts.FirstOrDefault();
    }

    private void NotifyDashboardPropertiesChanged()
    {
        OnPropertyChanged(nameof(NodeCount));
        OnPropertyChanged(nameof(TotalAccountCount));
        OnPropertyChanged(nameof(VisibleAccountCount));
        OnPropertyChanged(nameof(ManualRequiredCount));
        OnPropertyChanged(nameof(TotalActiveSessions));
        OnPropertyChanged(nameof(NodeThroughputLabel));
        OnPropertyChanged(nameof(AveragePingLabel));
        OnPropertyChanged(nameof(ConnectedNodesSummary));
        OnPropertyChanged(nameof(ProfilesLoadedLabel));
        OnPropertyChanged(nameof(ManualQueueLabel));
        OnPropertyChanged(nameof(AccountsGroupLabel));
        OnPropertyChanged(nameof(SourceBanner));
        OnPropertyChanged(nameof(SelectedNodeTableSummary));
        OnPropertyChanged(nameof(SelectedNodePanelTitle));
        OnPropertyChanged(nameof(SelectedNodePanelSummary));
        OnPropertyChanged(nameof(SelectedNodeIdentityLabel));
        OnPropertyChanged(nameof(SelectedNodeRuntimeLabel));
        OnPropertyChanged(nameof(SelectedNodeInstallCommand));
        OnPropertyChanged(nameof(SelectedNodeWorkerConfigJson));
        OnPropertyChanged(nameof(SelectedNodeDetailVisibility));
        OnPropertyChanged(nameof(SelectedNodePlaceholderVisibility));
        OnPropertyChanged(nameof(TableHint));
        OnPropertyChanged(nameof(DashboardViewVisibility));
        OnPropertyChanged(nameof(AccountsViewVisibility));
        OnPropertyChanged(nameof(NodesViewVisibility));
        OnPropertyChanged(nameof(ManualQueueViewVisibility));
        OnPropertyChanged(nameof(SettingsViewVisibility));
        OnPropertyChanged(nameof(IsDashboardActive));
        OnPropertyChanged(nameof(IsAccountsActive));
        OnPropertyChanged(nameof(IsNodesActive));
        OnPropertyChanged(nameof(IsManualQueueActive));
        OnPropertyChanged(nameof(IsSettingsActive));
        OnPropertyChanged(nameof(ManualQueueEmptyVisibility));
        OnPropertyChanged(nameof(ApiBaseUrlLabel));
        OnPropertyChanged(nameof(DataSourceModeLabel));
        OnPropertyChanged(nameof(SignalRStatusLabel));
    }

    public string SignalRStatusLabel
    {
        get => _signalRStatus;
        private set => SetProperty(ref _signalRStatus, value);
    }

    public string ApiBaseUrlLabel => _dataService.CurrentBaseUrl;
    public string DataSourceModeLabel => _dataService.CurrentModeLabel;

    public string? GetFocusedViewerUrl()
        => string.IsNullOrWhiteSpace(FocusedViewerUrl) ? null : FocusedViewerUrl;

    public string? GetSelectedNodeIdText()
        => SelectedNode?.NodeId.ToString();

    public string? GetSelectedNodeWorkerConfigJson()
        => SelectedNode is null ? null : SelectedNodeWorkerConfigJson;

    public string? GetSelectedNodeInstallCommand()
        => SelectedNode is null ? null : SelectedNodeInstallCommand;

    private async Task<NodeCommandStatusResponse?> WaitForCommandResultAsync(Guid nodeId, Guid commandId)
    {
        for (var attempt = 0; attempt < 12; attempt++)
        {
            var command = await _dataService.GetNodeCommandStatusAsync(nodeId, commandId);
            if (command is null)
            {
                await Task.Delay(500);
                continue;
            }

            if (command.Status is "Executed" or "Failed" or "TimedOut")
            {
                return command;
            }

            await Task.Delay(1000);
        }

        return await _dataService.GetNodeCommandStatusAsync(nodeId, commandId);
    }

    private void RefreshFocusedViewerState()
    {
        if (FocusedAccount is null)
        {
            UpdateFocusedViewer(null, "Request a viewer session to see the viewer URL here.");
            return;
        }

        if (_viewerSessions.TryGetValue(FocusedAccount.AccountId, out var viewerSession))
        {
            UpdateFocusedViewer(viewerSession.Url, viewerSession.Message);
            return;
        }

        UpdateFocusedViewer(null, "No viewer session has been requested for this account yet.");
    }

    private void UpdateFocusedViewer(string? url, string message)
    {
        if (SetProperty(ref _focusedViewerUrl, url ?? string.Empty, nameof(FocusedViewerUrl)))
        {
            OnPropertyChanged(nameof(FocusedViewerUrlVisibility));
        }

        SetProperty(ref _focusedViewerMessage, message, nameof(FocusedViewerMessage));
        OnPropertyChanged(nameof(FocusedViewerPanelVisibility));
    }

    private List<AccountCardViewModel> GetActionTargets()
    {
        var checkedAccounts = Accounts.Where(account => account.IsChecked).ToList();
        if (checkedAccounts.Count > 0)
        {
            return checkedAccounts;
        }

        return SelectedAccount is null ? new List<AccountCardViewModel>() : new List<AccountCardViewModel> { SelectedAccount };
    }

    private static string BuildAccountPayload(AccountCardViewModel account)
        => JsonSerializer.Serialize(new
        {
            accountId = account.AccountId,
            email = account.Email,
            username = account.Username
        });

    private static string? ExtractViewerUrl(string? resultMessage)
    {
        if (string.IsNullOrWhiteSpace(resultMessage))
        {
            return null;
        }

        var match = Regex.Match(resultMessage, @"https?://\S+", RegexOptions.IgnoreCase);
        return match.Success ? match.Value.Trim() : null;
    }

    private string? NormalizeViewerUrl(string? viewerUrl, AccountCardViewModel account)
    {
        if (string.IsNullOrWhiteSpace(viewerUrl) || !Uri.TryCreate(viewerUrl, UriKind.Absolute, out var uri))
        {
            return viewerUrl;
        }

        if (!IsLoopbackLikeHost(uri.Host))
        {
            return viewerUrl;
        }

        var replacementHost = ResolveViewerHost(account);
        if (string.IsNullOrWhiteSpace(replacementHost))
        {
            return viewerUrl;
        }

        var builder = new UriBuilder(uri)
        {
            Host = replacementHost
        };

        return builder.Uri.ToString();
    }

    private string? ResolveViewerHost(AccountCardViewModel account)
    {
        if (Uri.TryCreate(_dataService.CurrentBaseUrl, UriKind.Absolute, out var apiUri)
            && !IsLoopbackLikeHost(apiUri.Host))
        {
            return apiUri.Host;
        }

        if (!string.IsNullOrWhiteSpace(account.NodeIpAddress))
        {
            return account.NodeIpAddress.Trim();
        }

        return null;
    }

    private static bool IsLoopbackLikeHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
            || string.Equals(host, "0.0.0.0", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IPAddress.TryParse(host, out var ipAddress) && IPAddress.IsLoopback(ipAddress);
    }

    private static bool IsManualQueueCandidate(AccountCardViewModel account)
        => account.RequiresManualAttention || !string.IsNullOrWhiteSpace(account.ActiveAlertTitle);

    private static bool MatchesSearch(AccountCardViewModel account, string searchText)
    {
        if (string.IsNullOrWhiteSpace(searchText))
        {
            return true;
        }

        return account.Email.Contains(searchText, StringComparison.OrdinalIgnoreCase)
            || account.Username.Contains(searchText, StringComparison.OrdinalIgnoreCase)
            || account.NodeIpAddress.Contains(searchText, StringComparison.OrdinalIgnoreCase)
            || account.Status.Contains(searchText, StringComparison.OrdinalIgnoreCase)
            || account.CurrentStage.Contains(searchText, StringComparison.OrdinalIgnoreCase)
            || account.RecentLogSummary.Contains(searchText, StringComparison.OrdinalIgnoreCase);
    }

    private static string EscapeCsv(string value)
    {
        var escaped = value.Replace("\"", "\"\"");
        return $"\"{escaped}\"";
    }

    private string BuildWorkerConfigJson(NodeCardViewModel node)
    {
        var payload = new
        {
            NodeId = node.NodeId,
            BackendBaseUrl = _dataService.CurrentBaseUrl,
            AgentVersion = string.IsNullOrWhiteSpace(node.AgentVersion) ? "1.0.0" : node.AgentVersion,
            HeartbeatIntervalSeconds = 15,
            CommandPollIntervalSeconds = 3,
            ControlPort = node.ControlPort,
            ConnectionState = "Connected",
            ConnectionTimeoutSeconds = 5,
            CommandScriptsPath = "/opt/fleetmanager-agent/commands"
        };

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
    }

    private static string BuildInstallCommand(NodeCardViewModel node, string apiBaseUrl)
    {
        // GitHub Repository - Details from your screenshot
        string githubUser = "chatgptdza-beep";
        string githubRepo = "FleetManager-Worker";
        
        string rawUrl = $"https://raw.githubusercontent.com/{githubUser}/{githubRepo}/main/install.sh";
        
        // Command for Linux (Ubuntu/CentOS)
        return $"ssh {node.SshUsername}@{node.IpAddress} 'curl -s {rawUrl} | sudo bash -s -- {node.NodeId} {apiBaseUrl}'";
    }

    private sealed record ViewerSessionInfo(string? Url, string Message);
}
