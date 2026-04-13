using System.Collections.ObjectModel;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using FleetManager.Contracts.Accounts;
using FleetManager.Contracts.Nodes;
using FleetManager.Contracts.Operations;
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
    private bool _showWorkerInboxHistory;
    private int _pendingWorkerEventCount;
    private bool _isInitializing;

    public ObservableCollection<NodeCardViewModel> Nodes { get; } = new();
    public ObservableCollection<AccountCardViewModel> Accounts { get; } = new();
    public ObservableCollection<AccountCardViewModel> ManualQueueAccounts { get; } = new();
    public ObservableCollection<WorkerInboxEventViewModel> WorkerInboxEvents { get; } = new();

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
    public Visibility WorkerInboxEmptyVisibility => WorkerInboxEvents.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

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
                OnPropertyChanged(nameof(FocusedProxySummary));
                OnPropertyChanged(nameof(FocusedProxyRotationSummary));
                OnPropertyChanged(nameof(FocusedProxyRotations));
                OnPropertyChanged(nameof(FocusedProxyHistoryVisibility));
                OnPropertyChanged(nameof(FocusedProxyHistoryEmptyVisibility));
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
    public int PendingWorkerEventCount => _pendingWorkerEventCount;
    public string WorkerInboxLabel => PendingWorkerEventCount.ToString("00");
    public string WorkerInboxModeSummary => _showWorkerInboxHistory
        ? $"Showing pending and acknowledged events ({WorkerInboxEvents.Count})"
        : $"Showing pending events only ({WorkerInboxEvents.Count})";
    public string WorkerInboxEmptyMessage => _showWorkerInboxHistory
        ? "No worker events have been recorded yet."
        : "No pending worker events are waiting for the desktop.";
    public string AccountsGroupLabel => $"{NodeCount} groups";
    public string SourceBanner => $"API: {_dataService.CurrentBaseUrl}";
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
    public string FocusedProxySummary => FocusedAccount?.ProxySummary ?? "Select an account to inspect its proxy pool.";
    public string FocusedProxyRotationSummary => FocusedAccount is null
        ? "No account selected."
        : FocusedAccount.ProxyRotations.Count == 0
            ? $"Proxy pool size: {FocusedAccount.ProxyCount}"
            : $"Recent proxy rotations: {FocusedAccount.ProxyRotations.Count}";
    public IReadOnlyList<AccountProxyRotationViewModel> FocusedProxyRotations => FocusedAccount is null
        ? Array.Empty<AccountProxyRotationViewModel>()
        : FocusedAccount.ProxyRotations.ToList();
    public Visibility FocusedProxyHistoryVisibility => FocusedAccount is { ProxyRotations.Count: > 0 } ? Visibility.Visible : Visibility.Collapsed;
    public Visibility FocusedProxyHistoryEmptyVisibility => FocusedAccount is null || FocusedAccount.ProxyRotations.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
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

    private readonly ISshProvisioningService _sshProvisioningService;

    public MainWindowViewModel()
        : this(new DashboardDataService(), new SshProvisioningService())
    {
    }

    public MainWindowViewModel(IDashboardDataService dataService, ISshProvisioningService sshProvisioningService)
    {
        _dataService = dataService;
        _sshProvisioningService = sshProvisioningService;
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
            await LoadWorkerInboxEventsAsync();

            SelectedNode = ResolveSelectedNode(preferredNodeId);
            await LoadAccountsForSelectedNodeAsync(preferredAccountId);
            StatusMessage = $"{Nodes.Count} nodes, {TotalAccountCount} accounts | {_dataService.CurrentBaseUrl}";
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

            _signalR.OnBotStatusChanged += HandleSignalRBotStatusChanged;
            _signalR.OnManualRequired += HandleSignalRManualRequired;
            _signalR.OnProxyRotated += HandleSignalRProxyRotated;
            _signalR.OnNodeHeartbeat += HandleSignalRNodeHeartbeat;
            _signalR.OnNodeStatusChanged += HandleSignalRNodeStatusChanged;
            _signalR.OnWorkerInboxEvent += HandleWorkerInboxEvent;
            _signalR.OnWorkerInboxEventRemoved += HandleWorkerInboxEventRemoved;

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
            _signalR.OnBotStatusChanged -= HandleSignalRBotStatusChanged;
            _signalR.OnManualRequired -= HandleSignalRManualRequired;
            _signalR.OnProxyRotated -= HandleSignalRProxyRotated;
            _signalR.OnNodeHeartbeat -= HandleSignalRNodeHeartbeat;
            _signalR.OnNodeStatusChanged -= HandleSignalRNodeStatusChanged;
            _signalR.OnWorkerInboxEvent -= HandleWorkerInboxEvent;
            _signalR.OnWorkerInboxEventRemoved -= HandleWorkerInboxEventRemoved;
            await _signalR.DisconnectAsync();
            SignalRStatusLabel = "Disconnected";
        }
        catch { /* cleanup best-effort */ }
    }

    private void HandleSignalRBotStatusChanged(Guid accountId, string status)
    {
        Application.Current?.Dispatcher?.InvokeAsync(async () =>
        {
            await RefreshAccountDataAsync(accountId);
            StatusMessage = $"Live: {status}";
        });
    }

    private void HandleSignalRManualRequired(Guid accountId, string vncUrl)
    {
        Application.Current?.Dispatcher?.InvokeAsync(async () =>
        {
            _viewerSessions[accountId] = new ViewerSessionInfo(vncUrl, $"Manual takeover ready: {vncUrl}");
            await RefreshAccountDataAsync(accountId);
            StatusMessage = "Live: Manual required for account";
        });
    }

    private void HandleSignalRProxyRotated(Guid accountId, int newIndex)
    {
        Application.Current?.Dispatcher?.InvokeAsync(async () =>
        {
            await RefreshAccountDataAsync(accountId);
            StatusMessage = $"Live: Proxy rotated (index {newIndex})";
        });
    }

    private void HandleSignalRNodeHeartbeat(NodeSummaryResponse node)
    {
        Application.Current?.Dispatcher?.InvokeAsync(() =>
        {
            ApplyNodeSummary(node);
            StatusMessage = $"Live: node heartbeat {node.Name}";
        });
    }

    private void HandleSignalRNodeStatusChanged(Guid nodeId, string status)
    {
        Application.Current?.Dispatcher?.InvokeAsync(() =>
        {
            ApplyNodeUpdate(nodeId, node => CloneNode(node, status: status));
            StatusMessage = $"Live: node status {status}";
        });
    }

    private void HandleWorkerInboxEvent(WorkerInboxEventResponse workerEvent)
    {
        Application.Current?.Dispatcher?.InvokeAsync(() =>
        {
            UpsertWorkerInboxEvent(workerEvent);
            StatusMessage = $"Live: {workerEvent.Title}";
        });
    }

    private void HandleWorkerInboxEventRemoved(Guid eventId)
    {
        Application.Current?.Dispatcher?.InvokeAsync(async () =>
        {
            if (_showWorkerInboxHistory)
            {
                await LoadWorkerInboxEventsAsync(pendingOnly: false);
            }
            else
            {
                RemoveWorkerInboxEvent(eventId);
            }

            StatusMessage = "Live: worker event acknowledged";
        });
    }

    private async Task RefreshAccountDataAsync(Guid? preferredAccountId = null)
    {
        if (!preferredAccountId.HasValue)
        {
            await ReloadAsync(SelectedNode?.NodeId);
            return;
        }

        var account = await _dataService.GetAccountAsync(preferredAccountId.Value);
        if (account is null)
        {
            RemoveAccountFromCache(preferredAccountId.Value);
            StatusMessage = SourceBanner;
            return;
        }

        UpsertAccountInCache(account, preferredAccountId);
        await RefreshNodeSummaryAsync(account.NodeId);

        if (SelectedAccount?.AccountId == account.Id || FocusedAccount?.AccountId == account.Id)
        {
            await LoadSelectedAccountDetailsAsync(account.Id);
        }
        else
        {
            StatusMessage = SourceBanner;
        }
    }

    private async Task LoadWorkerInboxEventsAsync(bool? pendingOnly = null)
    {
        var displayPendingOnly = pendingOnly ?? !_showWorkerInboxHistory;
        _showWorkerInboxHistory = !displayPendingOnly;

        var workerEvents = await _dataService.GetWorkerInboxEventsAsync(displayPendingOnly);
        WorkerInboxEvents.Clear();
        foreach (var workerEvent in workerEvents
                     .OrderByDescending(workerEvent => workerEvent.CreatedAtUtc)
                     .Select(WorkerInboxEventViewModel.FromContract))
        {
            WorkerInboxEvents.Add(workerEvent);
            TryRestoreViewerSessionFromWorkerEvent(workerEvent);
        }

        UpdatePendingWorkerEventCount();
        NotifyDashboardPropertiesChanged();
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

    public async Task AcknowledgeWorkerInboxEventAsync(WorkerInboxEventViewModel workerEvent)
    {
        var acknowledged = await _dataService.AcknowledgeWorkerInboxEventAsync(workerEvent.EventId);
        if (!acknowledged)
        {
            throw new InvalidOperationException("Worker event no longer exists.");
        }

        if (_showWorkerInboxHistory)
        {
            await LoadWorkerInboxEventsAsync(pendingOnly: false);
        }
        else
        {
            RemoveWorkerInboxEvent(workerEvent.EventId);
        }

        StatusMessage = $"Acknowledged worker event: {workerEvent.Title}";
    }

    public Task ShowPendingWorkerInboxAsync()
        => LoadWorkerInboxEventsAsync(pendingOnly: true);

    public Task ShowWorkerInboxHistoryAsync()
        => LoadWorkerInboxEventsAsync(pendingOnly: false);

    public async Task CreateAccountAsync(CreateAccountRequest request)
    {
        var created = await _dataService.CreateAccountAsync(request);
        await ReloadAsync(request.NodeId, created.Id);
        StatusMessage = $"Created {created.Email} | {SourceBanner}";
    }

    public async Task CreateNodeAsync(CreateNodeRequest request, IProgress<string>? installProgress = null)
    {
        StatusMessage = "Testing SSH Connection...";
        installProgress?.Report("%05");
        installProgress?.Report("Testing SSH connection...");
        if (!await _sshProvisioningService.TestConnectionAsync(request))
        {
            throw new InvalidOperationException("SSH Connection Failed. Verify credentials.");
        }
        installProgress?.Report("SSH connection OK.");
        installProgress?.Report("%10");

        // Register node in API first so it appears in the UI immediately
        StatusMessage = "Registering node with API...";
        installProgress?.Report("Registering node with API...");
        var created = await _dataService.CreateNodeAsync(request);
        await ReloadAsync(created.Id);
        StatusMessage = $"Node '{created.Name}' registered. Installing agent...";
        installProgress?.Report($"Node registered: {created.Id}");
        installProgress?.Report("%20");

        try
        {
            StatusMessage = "Checking if Agent is already running...";
            installProgress?.Report("Checking if agent is already running...");
            bool agentRunning = await _sshProvisioningService.IsAgentRunningAsync(request);
            installProgress?.Report("%25");

            if (!agentRunning)
            {
                StatusMessage = "Installing FleetManager Agent...";
                installProgress?.Report("Starting agent installation...");
                await _sshProvisioningService.InstallAgentAsync(request, _dataService.CurrentBaseUrl, installProgress);
            }
            else
            {
                installProgress?.Report("Agent already running — skipping install.");
            }
            installProgress?.Report("%80");

            StatusMessage = "Configuring agent appsettings and restarting worker...";
            installProgress?.Report("Configuring agent appsettings...");
            await _sshProvisioningService.ConfigureAgentAsync(request, created.Id, _dataService.CurrentBaseUrl, installProgress);
            installProgress?.Report("%95");

            await ReloadAsync(created.Id);
            StatusMessage = $"Added {created.Name} | NodeId: {created.Id} | Auto-Installer {(agentRunning ? "Skipped" : "Success")} | {SourceBanner}";
            installProgress?.Report($"Done. Node '{created.Name}' is ready.");
            installProgress?.Report("%99");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Node registered but agent install failed: {ex.Message}";
            installProgress?.Report($"ERROR: {ex.Message}");
            throw;
        }
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

    public async Task InjectProxiesAsync(AccountCardViewModel account, string rawProxies, bool replaceExisting = false)
    {
        var result = await _dataService.InjectProxiesAsync(account.AccountId, rawProxies, replaceExisting);
        await RefreshAccountDataAsync(account.AccountId);

        StatusMessage = replaceExisting
            ? $"Replaced {result.ClearedCount} proxies and injected {result.InjectedCount} into {account.Email} | Total proxies: {result.TotalProxies} | {SourceBanner}"
            : $"Injected {result.InjectedCount} proxies into {account.Email} | Total proxies: {result.TotalProxies} | {SourceBanner}";
    }

    public async Task RotateProxyNowAsync(AccountCardViewModel account)
    {
        var result = await _dataService.RotateProxyAsync(account.AccountId, "Manual rotation from Desktop");
        await RefreshAccountDataAsync(account.AccountId);
        StatusMessage = $"Rotated proxy for {account.Email} to slot {result.NewIndex + 1} | {SourceBanner}";
    }

    public async Task DeleteAccountAsync(AccountCardViewModel account)
    {
        // Try to wipe the profile and stop browser on the VPS if it's assigned
        if (account.NodeId != Guid.Empty)
        {
            try
            {
                await DispatchAccountCommandAsync(account, "StopBrowser");
            }
            catch { /* Best effort, ignore if offline */ }
        }

        var deleted = await _dataService.DeleteAccountAsync(account.AccountId);
        if (!deleted)
        {
            throw new InvalidOperationException("Account not found.");
        }

        await ReloadAsync(SelectedNode?.NodeId);
        StatusMessage = $"Deleted account {account.AccountId} | {SourceBanner}";
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
        if (_viewerSessions.TryGetValue(account.AccountId, out var existingViewer)
            && !string.IsNullOrWhiteSpace(existingViewer.Url))
        {
            await ReloadAsync(SelectedNode?.NodeId, account.AccountId);
            StatusMessage = $"Recovered pending viewer for {account.Email}: {existingViewer.Url} | {SourceBanner}";
            return;
        }

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
            if (account.NodeId != Guid.Empty)
            {
                try
                {
                    await DispatchAccountCommandAsync(account, "StopBrowser");
                }
                catch { /* Best effort */ }
            }

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
        RestorePendingWorkerSessions(_allAccounts);
        RefreshManualQueue();
        NotifyDashboardPropertiesChanged();
    }

    private async Task LoadAccountsForSelectedNodeAsync(Guid? preferredAccountId = null)
    {
        RebuildSelectedNodeAccounts(preferredAccountId);
        StatusMessage = SourceBanner;
        await Task.CompletedTask;
    }

    private async Task LoadSelectedAccountDetailsAsync(Guid accountId)
    {
        var details = await _dataService.GetAccountStageAlertsAsync(accountId);
        if (!string.IsNullOrWhiteSpace(details?.ActiveAlertMessage))
        {
            TryRestoreViewerSession(accountId, details.ActiveAlertMessage, details.Email);
        }

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

    private async Task RefreshNodeSummaryAsync(Guid nodeId)
    {
        var node = await _dataService.GetNodeAsync(nodeId);
        if (node is not null)
        {
            ApplyNodeSummary(node);
        }
    }

    private void UpsertAccountInCache(AccountSummaryResponse summary, Guid? preferredAccountId = null)
    {
        var replacement = AccountCardViewModel.FromSummary(summary);
        if (string.Equals(summary.Status, "Manual", StringComparison.OrdinalIgnoreCase)
            || string.Equals(summary.Status, "Paused", StringComparison.OrdinalIgnoreCase))
        {
            TryRestoreViewerSession(summary.Id, summary.ActiveAlertMessage, summary.Email);
        }

        var existingIndex = _allAccounts.FindIndex(account => account.AccountId == summary.Id);
        if (existingIndex >= 0)
        {
            replacement.IsChecked = _allAccounts[existingIndex].IsChecked;
            _allAccounts[existingIndex] = replacement;
        }
        else
        {
            _allAccounts.Add(replacement);
        }

        _allAccounts.Sort(static (left, right) => string.Compare(left.Email, right.Email, StringComparison.OrdinalIgnoreCase));
        RefreshManualQueue();
        RebuildSelectedNodeAccounts(SelectedNode?.NodeId == summary.NodeId ? preferredAccountId : null);
        NotifyDashboardPropertiesChanged();
    }

    private void RemoveAccountFromCache(Guid accountId)
    {
        _allAccounts.RemoveAll(account => account.AccountId == accountId);
        _viewerSessions.Remove(accountId);
        RefreshManualQueue();
        RebuildSelectedNodeAccounts();
        NotifyDashboardPropertiesChanged();
    }

    private void UpsertWorkerInboxEvent(WorkerInboxEventResponse response)
    {
        var replacement = WorkerInboxEventViewModel.FromContract(response);
        var existingIndex = WorkerInboxEvents
            .Select((workerEvent, index) => new { workerEvent, index })
            .FirstOrDefault(entry => entry.workerEvent.EventId == response.Id)?
            .index ?? -1;

        if (existingIndex >= 0)
        {
            WorkerInboxEvents[existingIndex] = replacement;
        }
        else
        {
            WorkerInboxEvents.Insert(0, replacement);
        }

        TryRestoreViewerSessionFromWorkerEvent(replacement);
        UpdatePendingWorkerEventCount();
        NotifyDashboardPropertiesChanged();
    }

    private void RemoveWorkerInboxEvent(Guid eventId)
    {
        var existing = WorkerInboxEvents.FirstOrDefault(workerEvent => workerEvent.EventId == eventId);
        if (existing is null)
        {
            return;
        }

        WorkerInboxEvents.Remove(existing);
        UpdatePendingWorkerEventCount();
        NotifyDashboardPropertiesChanged();
    }

    private void UpdatePendingWorkerEventCount()
    {
        _pendingWorkerEventCount = WorkerInboxEvents.Count(workerEvent =>
            string.Equals(workerEvent.Status, "Pending", StringComparison.OrdinalIgnoreCase));
    }

    private void TryRestoreViewerSessionFromWorkerEvent(WorkerInboxEventViewModel workerEvent)
    {
        if (!workerEvent.AccountId.HasValue || string.IsNullOrWhiteSpace(workerEvent.ActionUrl))
        {
            return;
        }

        if (_viewerSessions.ContainsKey(workerEvent.AccountId.Value))
        {
            return;
        }

        _viewerSessions[workerEvent.AccountId.Value] = new ViewerSessionInfo(
            workerEvent.ActionUrl,
            $"{workerEvent.Title}: {workerEvent.ActionUrl}");
    }

    private void RebuildSelectedNodeAccounts(Guid? preferredAccountId = null)
    {
        _selectedNodeAccounts.Clear();

        if (SelectedNode is not null)
        {
            _selectedNodeAccounts.AddRange(_allAccounts
                .Where(account => account.NodeId == SelectedNode.NodeId)
                .OrderBy(account => account.Email));
        }

        ApplyVisibleAccountsFilter(preferredAccountId);
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
        OnPropertyChanged(nameof(PendingWorkerEventCount));
        OnPropertyChanged(nameof(TotalActiveSessions));
        OnPropertyChanged(nameof(NodeThroughputLabel));
        OnPropertyChanged(nameof(AveragePingLabel));
        OnPropertyChanged(nameof(ConnectedNodesSummary));
        OnPropertyChanged(nameof(ProfilesLoadedLabel));
        OnPropertyChanged(nameof(ManualQueueLabel));
        OnPropertyChanged(nameof(WorkerInboxLabel));
        OnPropertyChanged(nameof(WorkerInboxModeSummary));
        OnPropertyChanged(nameof(WorkerInboxEmptyMessage));
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
        OnPropertyChanged(nameof(FocusedProxySummary));
        OnPropertyChanged(nameof(FocusedProxyRotationSummary));
        OnPropertyChanged(nameof(FocusedProxyRotations));
        OnPropertyChanged(nameof(FocusedProxyHistoryVisibility));
        OnPropertyChanged(nameof(FocusedProxyHistoryEmptyVisibility));
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
        OnPropertyChanged(nameof(WorkerInboxEmptyVisibility));
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

    private void RestorePendingWorkerSessions(IEnumerable<AccountCardViewModel> accounts)
    {
        foreach (var account in accounts)
        {
            if (!string.Equals(account.Status, "Manual", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(account.Status, "Paused", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            TryRestoreViewerSession(account.AccountId, account.ActiveAlertMessage, account.Email);
        }
    }

    private void TryRestoreViewerSession(Guid accountId, string? alertMessage, string? email)
    {
        var viewerUrl = ExtractViewerUrl(alertMessage);
        if (string.IsNullOrWhiteSpace(viewerUrl) || _viewerSessions.ContainsKey(accountId))
        {
            return;
        }

        _viewerSessions[accountId] = new ViewerSessionInfo(
            viewerUrl,
            $"Recovered pending worker takeover for {email ?? "account"}: {viewerUrl}");
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

    private void ApplyNodeUpdate(Guid nodeId, Func<NodeCardViewModel, NodeCardViewModel> update)
    {
        var index = Nodes
            .Select((node, idx) => new { node, idx })
            .FirstOrDefault(entry => entry.node.NodeId == nodeId);
        if (index is null)
        {
            return;
        }

        var updated = update(index.node);
        Nodes[index.idx] = updated;

        if (_selectedNode?.NodeId == nodeId)
        {
            _selectedNode = updated;
            OnPropertyChanged(nameof(SelectedNode));
        }

        NotifyDashboardPropertiesChanged();
    }

    private void ApplyNodeSummary(NodeSummaryResponse node)
        => ApplyNodeUpdate(node.Id, _ => NodeCardViewModel.FromContract(node));

    private static NodeCardViewModel CloneNode(
        NodeCardViewModel source,
        string? status = null,
        DateTime? lastHeartbeatAtUtc = null,
        double? cpuPercent = null,
        double? ramPercent = null,
        double? diskPercent = null,
        int? activeSessions = null,
        int? pingMs = null,
        string? connectionState = null,
        string? agentVersion = null)
        => new()
        {
            NodeId = source.NodeId,
            Name = source.Name,
            Status = status ?? source.Status,
            IpAddress = source.IpAddress,
            SshPort = source.SshPort,
            SshUsername = source.SshUsername,
            AuthType = source.AuthType,
            OsType = source.OsType,
            Region = source.Region,
            LastHeartbeatAtUtc = lastHeartbeatAtUtc ?? source.LastHeartbeatAtUtc,
            CpuPercent = cpuPercent ?? source.CpuPercent,
            RamPercent = ramPercent ?? source.RamPercent,
            DiskPercent = diskPercent ?? source.DiskPercent,
            RamUsedGb = source.RamUsedGb,
            StorageUsedGb = source.StorageUsedGb,
            PingMs = pingMs ?? source.PingMs,
            ActiveSessions = activeSessions ?? source.ActiveSessions,
            ControlPort = source.ControlPort,
            ConnectionState = connectionState ?? source.ConnectionState,
            ConnectionTimeoutSeconds = source.ConnectionTimeoutSeconds,
            AssignedAccountCount = source.AssignedAccountCount,
            RunningAccounts = source.RunningAccounts,
            ManualAccounts = source.ManualAccounts,
            AlertAccounts = source.AlertAccounts,
            AgentVersion = agentVersion ?? source.AgentVersion
        };

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
            Agent = new
            {
                NodeId = node.NodeId,
                BackendBaseUrl = _dataService.CurrentBaseUrl,
                AgentVersion = string.IsNullOrWhiteSpace(node.AgentVersion) ? "1.0.0" : node.AgentVersion,
                HeartbeatIntervalSeconds = 15,
                CommandPollIntervalSeconds = 3,
                ControlPort = node.ControlPort,
                ConnectionState = "Connected",
                ConnectionTimeoutSeconds = 5,
                CommandScriptsPath = "/opt/fleetmanager-agent/commands",
                ApiKey = Environment.GetEnvironmentVariable("FLEETMANAGER_AGENT_API_KEY") ?? "<set-agent-api-key>",
                NodeIpAddress = node.IpAddress
            }
        };

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
    }

    private static string BuildInstallCommand(NodeCardViewModel node, string apiBaseUrl)
    {
        var sshTarget = $"{node.SshUsername}@{node.IpAddress}";
        return
            $"scp -r out/agent {sshTarget}:/tmp/fleetmanager-agent\n" +
            $"scp -r deploy/linux {sshTarget}:/tmp/fleetmanager-deploy\n" +
            $"ssh {sshTarget} \"sudo bash /tmp/fleetmanager-deploy/install-worker-ubuntu.sh /tmp/fleetmanager-agent\"\n" +
            $"bash deploy/linux/register-node.sh --api {ShellEscapeSingleQuoted(apiBaseUrl.Trim())} --admin-password '<set-api-password>' --name {ShellEscapeSingleQuoted(node.Name)} --ip {ShellEscapeSingleQuoted(node.IpAddress)} --ssh-user {ShellEscapeSingleQuoted(node.SshUsername)} --os {ShellEscapeSingleQuoted(node.OsType)} --region {ShellEscapeSingleQuoted(string.IsNullOrWhiteSpace(node.Region) ? "local" : node.Region)}";
    }

    private static string ShellEscapeSingleQuoted(string value)
        => $"'{value.Replace("'", "'\"'\"'")}'";

    private sealed record ViewerSessionInfo(string? Url, string Message);
}
