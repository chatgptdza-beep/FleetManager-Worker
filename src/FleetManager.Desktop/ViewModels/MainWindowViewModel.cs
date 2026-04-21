using System.Collections.ObjectModel;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using FleetManager.Contracts.Accounts;
using FleetManager.Contracts.Configuration;
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
    private string _apiBaseUrlInput = string.Empty;
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

    public string ApiBaseUrlInput
    {
        get => _apiBaseUrlInput;
        set => SetProperty(ref _apiBaseUrlInput, value);
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
    public string SourceBanner => _dataService.HasConfiguredBaseUrl
        ? $"API: {_dataService.CurrentBaseUrl}"
        : "API: not configured";
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
    public string SelectedNodeInstallCommand => SelectedNode is null || !_dataService.HasConfiguredBaseUrl
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
    private readonly IDesktopNodeRegistry _nodeRegistry;

    public MainWindowViewModel()
        : this(new DashboardDataService(), new SshProvisioningService(), new DesktopNodeRegistry())
    {
    }

    public MainWindowViewModel(
        IDashboardDataService dataService,
        ISshProvisioningService sshProvisioningService,
        IDesktopNodeRegistry nodeRegistry)
    {
        _dataService = dataService;
        _sshProvisioningService = sshProvisioningService;
        _nodeRegistry = nodeRegistry;
        _apiBaseUrlInput = _dataService.CurrentBaseUrl;
    }

    /// <summary>Exposes the data service for use in code-behind (e.g. RemoteTakeoverWindow).</summary>
    public IDashboardDataService DataService => _dataService;

    public async Task InitializeAsync()
    {
        await RestoreDesktopRuntimeStateAsync();
        if (!_dataService.HasConfiguredBaseUrl)
        {
            SignalRStatusLabel = "Not configured";
            ResetDashboardCollections();
            StatusMessage = "No real API server configured yet. Add a VPS with a real server address.";
            NotifyDashboardPropertiesChanged();
            return;
        }

        try
        {
            await ReloadAsync();
            await TryConnectSignalRAsync();
            await PersistDesktopRuntimeStateAsync();
        }
        catch (Exception ex) when (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FLEETMANAGER_API_BASE_URL")))
        {
            await DisconnectSignalRAsync();
            ClearConfiguredApiBaseUrl();
            ResetDashboardCollections();
            SignalRStatusLabel = "Not configured";
            await PersistDesktopRuntimeStateAsync();
            StatusMessage = $"Saved API endpoint was unreachable and has been cleared. Add a VPS or enter a new API URL. Details: {ex.Message}";
            NotifyDashboardPropertiesChanged();
        }
    }

    public async Task ReloadAsync(Guid? preferredNodeId = null, Guid? preferredAccountId = null)
    {
        if (!_dataService.HasConfiguredBaseUrl)
        {
            ResetDashboardCollections();
            SignalRStatusLabel = "Not configured";
            StatusMessage = "No real API server configured yet. Add a VPS with a real server address.";
            NotifyDashboardPropertiesChanged();
            return;
        }

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
        if (!_dataService.HasConfiguredBaseUrl)
        {
            SignalRStatusLabel = "Not configured";
            return;
        }

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
            var normalizedViewerUrl = NormalizeViewerUrl(vncUrl, accountId);
            _viewerSessions[accountId] = new ViewerSessionInfo(
                normalizedViewerUrl,
                $"Manual takeover ready: {normalizedViewerUrl}");
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
            await _nodeRegistry.RemoveByRemoteNodeIdAsync(nodeId);
            await ReloadAsync();
            StatusMessage = "VPS node deleted successfully";
        }
        else
        {
            StatusMessage = "Failed to delete VPS node";
        }
    }

    public async Task UpdateSelectedNodeAgentAsync()
    {
        var node = SelectedNode ?? throw new InvalidOperationException("Select a VPS first.");
        var bundleUrl = DesktopEnvironment.ResolveAgentBundleUrl();
        if (string.IsNullOrWhiteSpace(bundleUrl))
        {
            throw new InvalidOperationException("No agent release URL is configured.");
        }

        var payload = new Dictionary<string, object?>
        {
            ["bundleUrl"] = bundleUrl,
            ["bundleSha256Url"] = DesktopEnvironment.ResolveAgentBundleSha256Url(),
            ["bundleSha256"] = DesktopEnvironment.ResolveAgentBundleSha256(),
            ["restartDelaySeconds"] = 8
        };

        var commandId = await _dataService.DispatchNodeCommandAsync(node.NodeId, new DispatchNodeCommandRequest
        {
            CommandType = "UpdateAgentPackage",
            PayloadJson = JsonSerializer.Serialize(payload)
        });

        if (!commandId.HasValue)
        {
            throw new InvalidOperationException("UpdateAgentPackage command dispatch failed.");
        }

        var command = await WaitForCommandResultAsync(node.NodeId, commandId.Value);
        if (command is null)
        {
            throw new InvalidOperationException("UpdateAgentPackage did not return a status update.");
        }

        if (command.Status is "Failed" or "TimedOut")
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(command.ResultMessage)
                ? "UpdateAgentPackage failed."
                : command.ResultMessage);
        }

        await Task.Delay(1500);
        await ReloadAsync(node.NodeId);
        StatusMessage = string.IsNullOrWhiteSpace(command.ResultMessage)
            ? $"Agent self-update queued for {node.Name} | {SourceBanner}"
            : $"{command.ResultMessage.Trim()} | {SourceBanner}";
    }

    public async Task UpdateSelectedNodeStackAsync()
    {
        var node = SelectedNode ?? throw new InvalidOperationException("Select a VPS first.");
        var agentBundleUrl = DesktopEnvironment.ResolveAgentBundleUrl();
        if (string.IsNullOrWhiteSpace(agentBundleUrl))
        {
            throw new InvalidOperationException("No agent release URL is configured.");
        }

        var apiBundleUrl = DesktopEnvironment.ResolveApiBundleUrl();
        var restartDelaySeconds = 8;
        var apiRestartDelaySeconds = 12;

        var payload = new Dictionary<string, object?>
        {
            ["bundleUrl"] = agentBundleUrl,
            ["bundleSha256Url"] = DesktopEnvironment.ResolveAgentBundleSha256Url(),
            ["bundleSha256"] = DesktopEnvironment.ResolveAgentBundleSha256(),
            ["apiBundleUrl"] = string.IsNullOrWhiteSpace(apiBundleUrl) ? null : apiBundleUrl,
            ["apiBundleSha256Url"] = DesktopEnvironment.ResolveApiBundleSha256Url(),
            ["apiBundleSha256"] = DesktopEnvironment.ResolveApiBundleSha256(),
            ["restartDelaySeconds"] = restartDelaySeconds,
            ["apiRestartDelaySeconds"] = apiRestartDelaySeconds
        };

        try
        {
            var commandId = await _dataService.DispatchNodeCommandAsync(node.NodeId, new DispatchNodeCommandRequest
            {
                CommandType = "UpdateNodeStack",
                PayloadJson = JsonSerializer.Serialize(payload)
            });

            if (!commandId.HasValue)
            {
                throw new InvalidOperationException("UpdateNodeStack command dispatch failed.");
            }

            var command = await WaitForCommandResultAsync(node.NodeId, commandId.Value);
            if (command is null)
            {
                throw new InvalidOperationException("UpdateNodeStack did not return a status update.");
            }

            if (command.Status is "Failed" or "TimedOut")
            {
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(command.ResultMessage)
                    ? "UpdateNodeStack failed."
                    : command.ResultMessage);
            }

            await WaitForNodeRecoveryAfterStackUpdateAsync(node, restartDelaySeconds, apiRestartDelaySeconds);
            StatusMessage = string.IsNullOrWhiteSpace(command.ResultMessage)
                ? $"Stack self-update queued for {node.Name} | {SourceBanner}"
                : $"{command.ResultMessage.Trim()} | {SourceBanner}";
        }
        catch (InvalidOperationException ex) when (LooksLikeLegacyStackDispatchFailure(ex))
        {
            if (IsCurrentApiHost(node))
            {
                if (await TryBootstrapLegacyCurrentApiHostAsync(node))
                {
                    return;
                }

                throw new InvalidOperationException(
                    "This VPS hosts the current FleetManager API and is still on a legacy build that predates stack self-update. " +
                    "No SSH credentials are stored locally, so the desktop cannot bootstrap it automatically. " +
                    "Open Node Registry, save the SSH password or key for this VPS once, then run Self Update Stack again.");
            }

            try
            {
                await UpdateSelectedNodeAgentAsync();
                StatusMessage = $"Legacy control-plane detected. Worker self-update finished for {node.Name}. The primary API host still needs a one-time bootstrap. | {SourceBanner}";
            }
            catch (InvalidOperationException agentEx) when (LooksLikeMissingLegacyWorkerUpdateScript(agentEx))
            {
                throw new InvalidOperationException(
                    "This VPS is running a legacy worker that does not include UpdateAgentPackage.sh yet. " +
                    "Reinstall or bootstrap the worker once over SSH, then future updates will come from GitHub Releases.");
            }
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

    public async Task CreateNodeAsync(CreateNodeRequest request, IProgress<string>? installProgress = null, CancellationToken cancellationToken = default)
    {
        request.SshUsername = "root";
        StatusMessage = "Testing SSH Connection...";
        installProgress?.Report("%05");
        installProgress?.Report("Testing SSH connection...");
        if (!await _sshProvisioningService.TestConnectionAsync(request, cancellationToken))
        {
            throw new InvalidOperationException("SSH Connection Failed. Verify credentials.");
        }
        installProgress?.Report("SSH connection OK.");
        installProgress?.Report("%10");
        cancellationToken.ThrowIfCancellationRequested();

        if (!_dataService.HasConfiguredBaseUrl)
        {
            installProgress?.Report("No API configured yet. Preparing the entered VPS as the primary API server...");
            await EnsurePrimaryApiOnEnteredNodeAsync(request, installProgress, cancellationToken);
        }

        // Register node in API first so it appears in the UI immediately
        StatusMessage = "Registering node with API...";
        installProgress?.Report("Registering node with API...");
        var created = await CreateNodeWithApiAutoRecoveryAsync(request, installProgress, cancellationToken);
        await _nodeRegistry.UpsertProvisionedNodeAsync(
            request,
            created,
            _dataService.HasConfiguredBaseUrl ? _dataService.CurrentBaseUrl : null,
            cancellationToken);
        await ReloadAsync(created.Id);
        StatusMessage = $"Node '{created.Name}' registered. Installing agent...";
        installProgress?.Report($"Node registered: {created.Id}");
        installProgress?.Report("%20");
        cancellationToken.ThrowIfCancellationRequested();

        // Determine the API URL the remote agent should connect to.
        // If the desktop is talking to localhost, the VPS obviously cannot reach that.
        // Resolve via environment variable, or prompt the user to set one.
        var agentApiUrl = ResolveAgentApiUrl(_dataService.CurrentBaseUrl, request.IpAddress);

        try
        {
            StatusMessage = "Checking if Agent is already running...";
            installProgress?.Report("Checking if agent is already running...");
            bool agentRunning = await _sshProvisioningService.IsAgentRunningAsync(request, cancellationToken);
            installProgress?.Report("%25");
            cancellationToken.ThrowIfCancellationRequested();

            if (!agentRunning)
            {
                StatusMessage = "Installing FleetManager Agent...";
                installProgress?.Report("Starting agent installation...");
                await _sshProvisioningService.InstallAgentAsync(request, agentApiUrl, installProgress, cancellationToken);
            }
            else
            {
                installProgress?.Report("Agent already running — skipping install.");
            }
            installProgress?.Report("%80");
            cancellationToken.ThrowIfCancellationRequested();

            StatusMessage = "Configuring agent appsettings and restarting worker...";
            installProgress?.Report("Configuring agent appsettings...");
            installProgress?.Report($"Agent API URL: {agentApiUrl}");
            await _sshProvisioningService.ConfigureAgentAsync(request, created.Id, agentApiUrl, installProgress, cancellationToken);
            installProgress?.Report("%95");

            installProgress?.Report("Waiting for first heartbeat from VPS agent...");
            await WaitForFirstHeartbeatAsync(created.Id, installProgress, cancellationToken);

            // Mark the node as Online now that agent is installed and configured.
            // The agent heartbeat will maintain the status going forward.
            installProgress?.Report("Updating node status to Online...");
            await _dataService.UpdateNodeStatusAsync(created.Id, "Online");
            installProgress?.Report("%98");

            await ReloadAsync(created.Id);
            StatusMessage = $"Added {created.Name} | NodeId: {created.Id} | Auto-Installer {(agentRunning ? "Skipped" : "Success")} | {SourceBanner}";
            installProgress?.Report($"Done. Node '{created.Name}' is ready.");
            installProgress?.Report("%99");
        }
        catch (OperationCanceledException)
        {
            StatusMessage = $"Node '{created.Name}' install cancelled.";
            installProgress?.Report("Installation cancelled by user.");
            throw;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Node registered but agent install failed: {ex.Message}";
            installProgress?.Report($"ERROR: {ex.Message}");
            throw;
        }
    }

    private async Task<NodeSummaryResponse> CreateNodeWithApiAutoRecoveryAsync(
        CreateNodeRequest request,
        IProgress<string>? installProgress,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _dataService.CreateNodeAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            if (_dataService.HasConfiguredBaseUrl)
            {
                var currentApiProbe = await ProbeApiEndpointAsync(_dataService.CurrentBaseUrl, cancellationToken);
                if (!currentApiProbe.IsReachable)
                {
                    installProgress?.Report(
                        $"Configured API {_dataService.CurrentBaseUrl} is unreachable ({currentApiProbe.Detail}). " +
                        "Promoting the entered VPS as the new primary API...");
                    await DisconnectSignalRAsync();
                    ClearConfiguredApiBaseUrl();
                    await PersistDesktopRuntimeStateAsync();
                    await EnsurePrimaryApiOnEnteredNodeAsync(request, installProgress, cancellationToken);
                    installProgress?.Report("Retrying node registration with the new primary API...");
                    return await _dataService.CreateNodeAsync(request, cancellationToken);
                }
            }

            if (!ShouldTryNodeApiFallback(request.IpAddress))
            {
                throw;
            }

            installProgress?.Report("API registration failed on localhost. Trying VPS API endpoint automatically...");

            if (!await TrySwitchDesktopApiToNodeApiAsync(request, installProgress, cancellationToken))
            {
                throw new InvalidOperationException(
                    "Node registration failed and automatic switch to VPS API endpoint did not succeed.", ex);
            }

            installProgress?.Report("Retrying node registration with VPS API endpoint...");
            return await _dataService.CreateNodeAsync(request, cancellationToken);
        }
    }

    private bool ShouldTryNodeApiFallback(string vpsIp)
    {
        if (string.IsNullOrWhiteSpace(vpsIp))
        {
            return false;
        }

        if (!Uri.TryCreate(_dataService.CurrentBaseUrl, UriKind.Absolute, out var apiUri))
        {
            return false;
        }

        return string.Equals(apiUri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
            || string.Equals(apiUri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<bool> TrySwitchDesktopApiToNodeApiAsync(
        CreateNodeRequest request,
        IProgress<string>? installProgress,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(_dataService.CurrentBaseUrl, UriKind.Absolute, out var currentApiUri))
        {
            return false;
        }

        var candidatePort = currentApiUri.IsDefaultPort ? 5000 : currentApiUri.Port;
        var candidateApiBase = $"{currentApiUri.Scheme}://{request.IpAddress.Trim()}:{candidatePort}/";
        var previousApiBase = _dataService.CurrentBaseUrl;

        if (string.Equals(candidateApiBase, previousApiBase, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        SetConfiguredApiBaseUrl(candidateApiBase);
        installProgress?.Report($"Switched desktop API to {candidateApiBase}");

        try
        {
            await _dataService.GetNodesAsync(cancellationToken);
            await PersistDesktopRuntimeStateAsync();
            StatusMessage = $"API switched automatically to {candidateApiBase}";
            return true;
        }
        catch (Exception switchEx)
        {
            SetConfiguredApiBaseUrl(previousApiBase);
            await PersistDesktopRuntimeStateAsync();
            installProgress?.Report($"Auto-switch failed: {switchEx.Message}");
            return false;
        }
    }

    private async Task EnsurePrimaryApiOnEnteredNodeAsync(
        CreateNodeRequest request,
        IProgress<string>? installProgress,
        CancellationToken cancellationToken)
    {
        if (!TryBuildRemoteApiBaseUrl(request.IpAddress, 5000, out var candidateApiBase))
        {
            throw new InvalidOperationException("The entered VPS address is not a valid remote API host.");
        }

        installProgress?.Report($"Primary API candidate: {candidateApiBase}");
        var initialProbe = await ProbeApiEndpointAsync(candidateApiBase, cancellationToken);
        if (initialProbe.IsReachable)
        {
            installProgress?.Report("Primary API health probe succeeded.");
            if (await TryUsePrimaryApiAsync(candidateApiBase, installProgress, cancellationToken))
            {
                return;
            }

            throw new InvalidOperationException(
                $"FleetManager.Api is reachable on {candidateApiBase} but desktop authentication failed. " +
                "Check FLEETMANAGER_API_PASSWORD/AdminPassword and retry.");
        }

        installProgress?.Report($"Primary API probe failed: {initialProbe.Detail}");
        installProgress?.Report("FleetManager.Api is not reachable yet. Deploying it automatically on the entered VPS...");
        StatusMessage = "Deploying FleetManager.Api to the VPS...";
        await _sshProvisioningService.InstallApiAsync(request, installProgress, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        installProgress?.Report("Remote API deployment finished. Probing it from this desktop...");
        var deployedProbe = await ProbeApiEndpointAsync(candidateApiBase, cancellationToken);
        if (!deployedProbe.IsReachable)
        {
            throw new InvalidOperationException(
                $"FleetManager.Api was installed on {candidateApiBase} but this desktop still cannot reach it. " +
                "Open TCP 5000 on the VPS/provider firewall and retry.");
        }

        installProgress?.Report("Primary API health probe succeeded after deployment.");
        if (await TryUsePrimaryApiAsync(candidateApiBase, installProgress, cancellationToken))
        {
            return;
        }

        throw new InvalidOperationException(
            $"FleetManager.Api is reachable on {candidateApiBase} but desktop authentication still failed. " +
            "Check FLEETMANAGER_API_PASSWORD/AdminPassword and retry.");
    }

    private async Task<bool> TryUsePrimaryApiAsync(
        string candidateApiBase,
        IProgress<string>? installProgress,
        CancellationToken cancellationToken)
    {
        SetConfiguredApiBaseUrl(candidateApiBase);

        try
        {
            await _dataService.GetNodesAsync(cancellationToken);
            await PersistDesktopRuntimeStateAsync();
            StatusMessage = $"Primary API set to {candidateApiBase}";
            return true;
        }
        catch (Exception apiEx)
        {
            ClearConfiguredApiBaseUrl();
            await PersistDesktopRuntimeStateAsync();
            installProgress?.Report($"Primary API auth failed: {apiEx.Message}");
            return false;
        }
    }

    private static async Task<ApiEndpointProbeResult> ProbeApiEndpointAsync(
        string apiBaseUrl,
        CancellationToken cancellationToken)
    {
        using var client = new HttpClient
        {
            BaseAddress = new Uri(apiBaseUrl, UriKind.Absolute),
            Timeout = TimeSpan.FromSeconds(8)
        };

        try
        {
            using var response = await client.GetAsync("health", cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return new ApiEndpointProbeResult(true, "OK");
            }

            return new ApiEndpointProbeResult(
                false,
                $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new ApiEndpointProbeResult(false, ex.Message);
        }
    }

    /// <summary>
    /// Resolves the API URL the remote agent should use to connect back.
    /// If the Desktop is connected to localhost, we look for FLEETMANAGER_PUBLIC_API_URL
    /// or derive a URL from the API server's own binding.
    /// </summary>
    private static string ResolveAgentApiUrl(string desktopApiUrl, string vpsIp)
    {
        // If an override is explicitly set for the agent, always use it
        var publicOverride = Environment.GetEnvironmentVariable("FLEETMANAGER_PUBLIC_API_URL");
        if (!string.IsNullOrWhiteSpace(publicOverride))
            return publicOverride.TrimEnd('/') + "/";

        if (!Uri.TryCreate(desktopApiUrl, UriKind.Absolute, out var apiUri))
            throw new InvalidOperationException(
                "Desktop API URL is invalid. Set a valid API base URL or FLEETMANAGER_PUBLIC_API_URL.");

        // If the Desktop is connected directly to the same VPS IP, force localhost
        // on the VPS for reliability and to avoid public routing/NAT issues.
        if (string.Equals(apiUri.Host, vpsIp, StringComparison.OrdinalIgnoreCase))
        {
            return $"{apiUri.Scheme}://127.0.0.1:{apiUri.Port}/";
        }

        // If the desktop is NOT talking to localhost, the agent can use the same URL
        if (!string.Equals(apiUri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(apiUri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase))
        {
            return desktopApiUrl;
        }

        // Desktop is talking to localhost — the remote VPS can't reach that.
        // Build a URL using the machine's public IP on the same port.
        var machineIp = Environment.GetEnvironmentVariable("FLEETMANAGER_HOST_IP");
        if (!string.IsNullOrWhiteSpace(machineIp))
        {
            return $"{apiUri.Scheme}://{machineIp.Trim()}:{apiUri.Port}/";
        }

        // Zero-touch fallback: if operator entered a VPS IP and Desktop API is localhost,
        // assume the API is hosted on that VPS and use it automatically.
        if (!string.IsNullOrWhiteSpace(vpsIp))
        {
            var normalizedVpsIp = vpsIp.Trim();
            if (Uri.CheckHostName(normalizedVpsIp) is UriHostNameType.IPv4 or UriHostNameType.IPv6 or UriHostNameType.Dns)
            {
                return $"{apiUri.Scheme}://{normalizedVpsIp}:{apiUri.Port}/";
            }
        }

        throw new InvalidOperationException(
            "Desktop API is localhost, which remote VPS cannot reach. " +
            "Use VPS API URL in app settings or set FLEETMANAGER_PUBLIC_API_URL.");
    }

    private async Task WaitForFirstHeartbeatAsync(Guid nodeId, IProgress<string>? installProgress, CancellationToken cancellationToken)
    {
        const int maxAttempts = 45; // about 90 seconds total
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var node = await _dataService.GetNodeAsync(nodeId, cancellationToken);
            if (node?.LastHeartbeatAtUtc is not null)
            {
                installProgress?.Report($"Heartbeat received at {node.LastHeartbeatAtUtc:O}");
                return;
            }

            if (attempt % 5 == 0)
            {
                installProgress?.Report($"Still waiting for heartbeat... ({attempt * 2}s)");
            }

            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }

        throw new InvalidOperationException(
            "Agent installed but no heartbeat was received. Check API URL mapping and agent service logs on VPS.");
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
        var snapshot = CreateDesktopConfigSnapshot();

        var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(path, json);
        await DesktopRuntimeStateStore.SaveAsync(snapshot);
        StatusMessage = $"Saved desktop config to {Path.GetFileName(path)} | {SourceBanner}";
    }

    public async Task LoadDesktopConfigAsync(string path)
    {
        var json = await File.ReadAllTextAsync(path);
        var snapshot = JsonSerializer.Deserialize<DesktopConfigSnapshot>(json)
            ?? throw new InvalidOperationException("Desktop config is invalid.");

        SearchText = snapshot.SearchText ?? string.Empty;

        if (string.IsNullOrWhiteSpace(snapshot.ApiBaseUrl))
        {
            await ClearApiBaseUrlAsync();
        }
        else
        {
            ApiBaseUrlInput = snapshot.ApiBaseUrl;
            await ApplyApiBaseUrlAsync();
            await DesktopRuntimeStateStore.SaveAsync(snapshot);
            await ReloadAsync(snapshot.SelectedNodeId);
        }

        StatusMessage = $"Loaded desktop config from {Path.GetFileName(path)} | {SourceBanner}";
    }

    public async Task ApplyApiBaseUrlAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ApiBaseUrlInput))
        {
            throw new InvalidOperationException("Enter an API base URL first, for example http://203.0.113.10:5000/.");
        }

        var previousBaseUrl = _dataService.HasConfiguredBaseUrl ? _dataService.CurrentBaseUrl : null;
        await DisconnectSignalRAsync();

        try
        {
            SetConfiguredApiBaseUrl(ApiBaseUrlInput);
            await ReloadAsync();
            await TryConnectSignalRAsync();
            await PersistDesktopRuntimeStateAsync();
            StatusMessage = $"API base URL updated to {_dataService.CurrentBaseUrl}";
        }
        catch
        {
            await RestorePreviousApiBaseUrlAsync(previousBaseUrl, cancellationToken);
            throw;
        }
    }

    public async Task ClearApiBaseUrlAsync()
    {
        await DisconnectSignalRAsync();
        ClearConfiguredApiBaseUrl();
        ResetDashboardCollections();
        SignalRStatusLabel = "Not configured";
        await PersistDesktopRuntimeStateAsync();
        StatusMessage = "API connection cleared. Add a VPS or enter a new API URL.";
        NotifyDashboardPropertiesChanged();
    }

    public async Task ResetLocalDesktopStateAsync(CancellationToken cancellationToken = default)
    {
        await DisconnectSignalRAsync();
        ClearConfiguredApiBaseUrl();
        ResetDashboardCollections();
        SearchText = string.Empty;
        SignalRStatusLabel = "Not configured";
        await _nodeRegistry.ClearAsync(cancellationToken);
        await DesktopRuntimeStateStore.DeleteAsync(cancellationToken);
        StatusMessage = "Local desktop state was reset. Add a new VPS to start a fresh environment.";
        NotifyDashboardPropertiesChanged();
    }

    private DesktopConfigSnapshot CreateDesktopConfigSnapshot()
        => new()
        {
            ApiBaseUrl = _dataService.CurrentBaseUrl,
            SelectedNodeId = SelectedNode?.NodeId,
            SearchText = SearchText
        };

    private async Task RestoreDesktopRuntimeStateAsync()
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FLEETMANAGER_API_BASE_URL")))
        {
            SyncApiBaseUrlInput();
            return;
        }

        var snapshot = await DesktopRuntimeStateStore.TryLoadAsync();
        if (snapshot is null || string.IsNullOrWhiteSpace(snapshot.ApiBaseUrl))
        {
            SearchText = snapshot?.SearchText ?? string.Empty;
            SyncApiBaseUrlInput();
            return;
        }

        try
        {
            SetConfiguredApiBaseUrl(snapshot.ApiBaseUrl);
            SearchText = snapshot.SearchText ?? string.Empty;
        }
        catch
        {
            ClearConfiguredApiBaseUrl();
            SearchText = snapshot.SearchText ?? string.Empty;
            await DesktopRuntimeStateStore.SaveAsync(CreateDesktopConfigSnapshot());
        }
    }

    private Task PersistDesktopRuntimeStateAsync()
        => DesktopRuntimeStateStore.SaveAsync(CreateDesktopConfigSnapshot());

    private void SetConfiguredApiBaseUrl(string baseUrl)
    {
        _dataService.ConfigureBaseUrl(baseUrl);
        SyncApiBaseUrlInput();
    }

    private void ClearConfiguredApiBaseUrl()
    {
        _dataService.ClearBaseUrl();
        SyncApiBaseUrlInput();
    }

    private void SyncApiBaseUrlInput()
    {
        ApiBaseUrlInput = _dataService.CurrentBaseUrl;
    }

    private async Task RestorePreviousApiBaseUrlAsync(string? previousBaseUrl, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(previousBaseUrl))
        {
            try
            {
                SetConfiguredApiBaseUrl(previousBaseUrl);
                await ReloadAsync();
                await TryConnectSignalRAsync();
                await PersistDesktopRuntimeStateAsync();
                StatusMessage = $"Restored previous API endpoint {previousBaseUrl}";
                return;
            }
            catch
            {
                // Fall back to a fully cleared desktop state below.
            }
        }

        ClearConfiguredApiBaseUrl();
        ResetDashboardCollections();
        SignalRStatusLabel = "Not configured";
        await PersistDesktopRuntimeStateAsync();
        NotifyDashboardPropertiesChanged();
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
            if (commandType == "FetchSessionLogs")
            {
                var summary = string.IsNullOrWhiteSpace(command.ResultMessage)
                    ? "No log output returned."
                    : command.ResultMessage.Trim();
                StatusMessage = $"Logs for {account.Email}: {summary} | {SourceBanner}";
            }
            else if (commandType == "OpenAssignedSession" && !string.IsNullOrWhiteSpace(command.ResultMessage))
            {
                StatusMessage = $"{command.ResultMessage.Trim()} | {SourceBanner}";
            }
            else
            {
                StatusMessage = $"{commandType} executed for {account.Email} | {SourceBanner}";
            }

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
           || string.Equals(commandType, "OpenAssignedSession", StringComparison.Ordinal)
           || string.Equals(commandType, "FetchSessionLogs", StringComparison.Ordinal);

    private async Task WaitForNodeRecoveryAfterStackUpdateAsync(
        NodeCardViewModel node,
        int agentRestartDelaySeconds,
        int apiRestartDelaySeconds)
    {
        var maxDelaySeconds = Math.Max(agentRestartDelaySeconds, IsCurrentApiHost(node) ? apiRestartDelaySeconds : 0);
        var firstProbeAt = DateTime.UtcNow.AddSeconds(Math.Max(4, maxDelaySeconds + 2));
        var deadline = firstProbeAt.AddSeconds(10);
        Exception? lastError = null;

        if (DateTime.UtcNow < firstProbeAt)
        {
            await Task.Delay(firstProbeAt - DateTime.UtcNow);
        }

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                await ReloadAsync(node.NodeId);
                return;
            }
            catch (Exception ex)
            {
                lastError = ex;
            }

            await Task.Delay(2000);
        }

        if (lastError is not null)
        {
            throw lastError;
        }

        await ReloadAsync(node.NodeId);
    }

    private bool IsCurrentApiHost(NodeCardViewModel node)
    {
        if (string.IsNullOrWhiteSpace(node.IpAddress))
        {
            return false;
        }

        return Uri.TryCreate(_dataService.CurrentBaseUrl, UriKind.Absolute, out var apiUri)
            && string.Equals(apiUri.Host, node.IpAddress.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private async Task<bool> TryBootstrapLegacyCurrentApiHostAsync(NodeCardViewModel node)
    {
        var managedNode = await _nodeRegistry.GetByRemoteNodeIdAsync(node.NodeId);
        if (managedNode is null || !managedNode.HasStoredCredentials)
        {
            return false;
        }

        var connectionRequest = _nodeRegistry.BuildConnectionRequest(managedNode);
        var progress = new Progress<string>(message =>
        {
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                StatusMessage = $"Legacy bootstrap: {message}";
            });
        });

        await DisconnectSignalRAsync();
        await _sshProvisioningService.InstallApiAsync(connectionRequest, progress);
        await _sshProvisioningService.InstallAgentAsync(connectionRequest, _dataService.CurrentBaseUrl, progress);
        await _sshProvisioningService.ConfigureAgentAsync(connectionRequest, node.NodeId, _dataService.CurrentBaseUrl, progress);
        await Task.Delay(2000);
        await ReloadAsync(node.NodeId);
        await TryConnectSignalRAsync();
        StatusMessage = $"Legacy API host {node.Name} bootstrapped successfully from SSH and is now GitHub-release updateable. | {SourceBanner}";
        return true;
    }

    private static bool LooksLikeLegacyStackDispatchFailure(InvalidOperationException exception)
        => exception.Message.StartsWith("Dispatch of UpdateNodeStack failed", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeMissingLegacyWorkerUpdateScript(InvalidOperationException exception)
        => exception.Message.Contains("UpdateAgentPackage.sh", StringComparison.OrdinalIgnoreCase)
           || exception.Message.Contains("Script not found", StringComparison.OrdinalIgnoreCase);

    private async Task LoadNodesAsync()
    {
        var nodes = await _dataService.GetNodesAsync();
        await _nodeRegistry.SyncRemoteNodesAsync(nodes, _dataService.CurrentBaseUrl);
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
        await _nodeRegistry.SyncTaskDataAsync(accounts);
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

        var viewerUrl = NormalizeViewerUrl(workerEvent.ActionUrl, workerEvent.AccountId.Value);
        _viewerSessions[workerEvent.AccountId.Value] = new ViewerSessionInfo(
            viewerUrl,
            $"{workerEvent.Title}: {viewerUrl}");
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

    public string ApiBaseUrlLabel => _dataService.HasConfiguredBaseUrl ? _dataService.CurrentBaseUrl : "Not configured";
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
        for (var attempt = 0; attempt < 45; attempt++)
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
        var viewerUrl = NormalizeViewerUrl(ExtractViewerUrl(alertMessage), accountId);
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

    private string? NormalizeViewerUrl(string? viewerUrl, Guid accountId)
    {
        var account = FindAccount(accountId);
        return NormalizeViewerUrl(viewerUrl, account);
    }

    private string? NormalizeViewerUrl(string? viewerUrl, AccountCardViewModel? account)
    {
        if (string.IsNullOrWhiteSpace(viewerUrl) || !Uri.TryCreate(viewerUrl, UriKind.Absolute, out var uri))
        {
            return viewerUrl;
        }

        var replacementHost = ResolveViewerHost(account);
        if (string.IsNullOrWhiteSpace(replacementHost))
        {
            return viewerUrl;
        }

        var normalizedHost = IsLoopbackLikeHost(uri.Host) ? replacementHost : uri.Host;
        var normalizedQuery = RewriteViewerQuery(uri.Query, replacementHost);
        if (string.Equals(normalizedHost, uri.Host, StringComparison.OrdinalIgnoreCase)
            && string.Equals(normalizedQuery, uri.Query.TrimStart('?'), StringComparison.Ordinal))
        {
            return viewerUrl;
        }

        var builder = new UriBuilder(uri)
        {
            Host = normalizedHost,
            Query = normalizedQuery
        };

        return builder.Uri.ToString();
    }

    private string? ResolveViewerHost(AccountCardViewModel? account)
    {
        if (Uri.TryCreate(_dataService.CurrentBaseUrl, UriKind.Absolute, out var apiUri)
            && !IsLoopbackLikeHost(apiUri.Host))
        {
            return apiUri.Host;
        }

        if (account is not null && !string.IsNullOrWhiteSpace(account.NodeIpAddress))
        {
            return account.NodeIpAddress.Trim();
        }

        return null;
    }

    private AccountCardViewModel? FindAccount(Guid accountId)
        => _allAccounts.FirstOrDefault(account => account.AccountId == accountId)
           ?? Accounts.FirstOrDefault(account => account.AccountId == accountId)
           ?? ManualQueueAccounts.FirstOrDefault(account => account.AccountId == accountId)
           ?? (FocusedAccount?.AccountId == accountId ? FocusedAccount : null)
           ?? (SelectedAccount?.AccountId == accountId ? SelectedAccount : null);

    private static string RewriteViewerQuery(string query, string replacementHost)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return string.Empty;
        }

        var segments = query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
        {
            return string.Empty;
        }

        var changed = false;
        var rewrittenSegments = new List<string>(segments.Length);
        foreach (var segment in segments)
        {
            var parts = segment.Split('=', 2);
            var key = Uri.UnescapeDataString(parts[0]);
            var value = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty;

            if (string.Equals(key, "host", StringComparison.OrdinalIgnoreCase) && IsLoopbackLikeHost(value))
            {
                value = replacementHost;
                changed = true;
            }

            rewrittenSegments.Add(parts.Length > 1
                ? $"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value)}"
                : Uri.EscapeDataString(key));
        }

        return changed ? string.Join("&", rewrittenSegments) : query.TrimStart('?');
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
                BackendBaseUrl = _dataService.HasConfiguredBaseUrl ? _dataService.CurrentBaseUrl : "<set-real-api-url>",
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
        var bundleUrl = ResolveInstallBundleUrl();
        var bundleSha256Url = ResolveInstallBundleSha256Url();
        var sshTarget = $"{node.SshUsername}@{node.IpAddress}";
        var bundleRemotePath = $"/tmp/{FleetManagerReleaseDefaults.AgentBundleFileName}";
        var bundleSha256RemotePath = $"/tmp/{FleetManagerReleaseDefaults.AgentBundleSha256FileName}";
        var verifyBundleCommand = string.IsNullOrWhiteSpace(bundleSha256Url)
            ? string.Empty
            : $"curl -fsSL {ShellEscapeSingleQuoted(bundleSha256Url)} -o {ShellEscapeSingleQuoted(bundleSha256RemotePath)} && " +
              $"(cd /tmp && sha256sum -c {ShellEscapeSingleQuoted(FleetManagerReleaseDefaults.AgentBundleSha256FileName)}) && ";

        return
            $"ssh {sshTarget} \"sudo apt-get update && sudo env DEBIAN_FRONTEND=noninteractive apt-get install -y curl unzip && " +
            $"curl -fsSL {ShellEscapeSingleQuoted(bundleUrl)} -o {ShellEscapeSingleQuoted(bundleRemotePath)} && " +
            verifyBundleCommand +
            "rm -rf /tmp/fleetmanager-bundle && mkdir -p /tmp/fleetmanager-bundle && " +
            $"unzip -oq {ShellEscapeSingleQuoted(bundleRemotePath)} -d /tmp/fleetmanager-bundle && " +
            "sudo bash /tmp/fleetmanager-bundle/deploy/linux/install-worker-ubuntu.sh /tmp/fleetmanager-bundle/agent && " +
            $"bash /tmp/fleetmanager-bundle/deploy/linux/register-node.sh --api {ShellEscapeSingleQuoted(apiBaseUrl.Trim())} --admin-password '<set-api-password>' --name {ShellEscapeSingleQuoted(node.Name)} --ip {ShellEscapeSingleQuoted(node.IpAddress)} --ssh-user {ShellEscapeSingleQuoted(node.SshUsername)} --os {ShellEscapeSingleQuoted(node.OsType)} --region {ShellEscapeSingleQuoted(string.IsNullOrWhiteSpace(node.Region) ? "local" : node.Region)}\"";
    }

    private void ResetDashboardCollections()
    {
        Nodes.Clear();
        Accounts.Clear();
        ManualQueueAccounts.Clear();
        WorkerInboxEvents.Clear();
        _allAccounts.Clear();
        _selectedNodeAccounts.Clear();
        SelectedNode = null;
        SelectedAccount = null;
        FocusedAccount = null;
        UpdatePendingWorkerEventCount();
    }

    private static bool TryBuildRemoteApiBaseUrl(string host, int port, out string apiBaseUrl)
    {
        apiBaseUrl = string.Empty;
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        var normalizedHost = host.Trim();
        if (string.Equals(normalizedHost, "localhost", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalizedHost, "0.0.0.0", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (IPAddress.TryParse(normalizedHost, out var ipAddress) && IPAddress.IsLoopback(ipAddress))
        {
            return false;
        }

        if (Uri.CheckHostName(normalizedHost) == UriHostNameType.Unknown)
        {
            return false;
        }

        apiBaseUrl = $"http://{normalizedHost}:{port}/";
        return true;
    }

    private static string ResolveInstallBundleUrl()
    {
        var bundleUrl = DesktopEnvironment.ResolveAgentBundleUrl();
        return string.IsNullOrWhiteSpace(bundleUrl)
            ? "<set-FLEETMANAGER_AGENT_BUNDLE_URL>"
            : bundleUrl;
    }

    private static string ResolveInstallBundleSha256Url()
    {
        return DesktopEnvironment.ResolveAgentBundleSha256Url();
    }

    private static string ShellEscapeSingleQuoted(string value)
        => $"'{value.Replace("'", "'\"'\"'")}'";

    private sealed record ApiEndpointProbeResult(bool IsReachable, string Detail);
    private sealed record ViewerSessionInfo(string? Url, string Message);
}
