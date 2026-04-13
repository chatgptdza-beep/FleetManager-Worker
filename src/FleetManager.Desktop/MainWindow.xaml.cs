using FleetManager.Contracts.Accounts;
using FleetManager.Desktop.ViewModels;
using FleetManager.Desktop.Views;
using Microsoft.Win32;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace FleetManager.Desktop;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private readonly Dictionary<Guid, InstallConsoleWindow> _installConsoles = new();
    private readonly Dictionary<Guid, CancellationTokenSource> _installCancellations = new();

    public MainWindow(MainWindowViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = _viewModel;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        await RunUiActionAsync(_viewModel.InitializeAsync);
    }

    protected override async void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        await _viewModel.DisconnectSignalRAsync();
    }

    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string viewName)
        {
            _viewModel.SwitchView(viewName);
        }
    }

    private async void LoadConfig_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*" };
        if (dialog.ShowDialog() == true)
        {
            await RunUiActionAsync(() => _viewModel.LoadDesktopConfigAsync(dialog.FileName));
        }
    }

    private async void SaveConfig_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "JSON files (*.json)|*.json", FileName = "fleetmanager.desktop.json" };
        if (dialog.ShowDialog() == true)
        {
            await RunUiActionAsync(() => _viewModel.SaveDesktopConfigAsync(dialog.FileName));
        }
    }

    private async void ExportLogs_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "CSV files (*.csv)|*.csv", FileName = "visible-accounts.csv" };
        if (dialog.ShowDialog() == true)
        {
            await RunUiActionAsync(() => _viewModel.ExportVisibleAccountsCsvAsync(dialog.FileName));
        }
    }

    private async void AddVps_Click(object sender, RoutedEventArgs e)
    {
        var editor = new VpsEditorWindow { Owner = this };
        if (editor.ShowDialog() == true)
        {
            var console = new InstallConsoleWindow { Owner = this };
            console.Show();

            var cts = new CancellationTokenSource();
            Guid? registeredNodeId = null;

            // Sync callback so first messages display immediately
            var trackingProgress = new Progress<string>(msg =>
            {
                console.AppendLog(msg);
                // Capture node ID from the registration message
                if (registeredNodeId == null && msg.StartsWith("Node registered:") && Guid.TryParse(msg.AsSpan("Node registered: ".Length), out var id))
                {
                    registeredNodeId = id;
                    _installConsoles[id] = console;
                    _installCancellations[id] = cts;
                    console.Closed += (_, _) =>
                    {
                        _installConsoles.Remove(id);
                        _installCancellations.Remove(id);
                    };
                }
            });

            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                await _viewModel.CreateNodeAsync(editor.Request, trackingProgress, cts.Token);
                console.MarkSuccess();
            }
            catch (OperationCanceledException)
            {
                console.MarkFailed("Installation was cancelled.");
            }
            catch (Exception ex)
            {
                console.MarkFailed(ex.Message);
            }
            finally
            {
                Mouse.OverrideCursor = null;
                if (registeredNodeId.HasValue)
                {
                    _installCancellations.Remove(registeredNodeId.Value);
                }
                cts.Dispose();
            }
        }
    }

    private void VpsCard_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is NodeCardViewModel node)
        {
            // If there's an active install console for this node, bring it to front
            if (_installConsoles.TryGetValue(node.NodeId, out var console))
            {
                console.Activate();
                console.Focus();
            }
        }
    }

    private void VpsContextOpenConsole_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.DataContext is NodeCardViewModel node)
        {
            if (_installConsoles.TryGetValue(node.NodeId, out var console))
            {
                console.Activate();
                console.Focus();
            }
            else
            {
                MessageBox.Show(this, "No install console is active for this VPS.", "FleetManager", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }

    private async void VpsContextDelete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem mi || mi.DataContext is not NodeCardViewModel node)
            return;

        var confirmed = MessageBox.Show(
            this,
            $"Delete VPS '{node.Name}'?\n\nThis will cancel any running installation and permanently remove this node.",
            "FleetManager",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirmed != MessageBoxResult.Yes)
            return;

        // Cancel any active install for this node
        if (_installCancellations.TryGetValue(node.NodeId, out var cts))
        {
            cts.Cancel();
            _installCancellations.Remove(node.NodeId);
        }
        if (_installConsoles.TryGetValue(node.NodeId, out var console))
        {
            console.MarkFailed("VPS deleted by user.");
            _installConsoles.Remove(node.NodeId);
        }

        await RunUiActionAsync(() => _viewModel.DeleteNodeAsync(node.NodeId));
    }

    private async void AddAccount_Click(object sender, RoutedEventArgs e)
    {
        var nodeName = _viewModel.SelectedNode?.NodeDisplayName ?? "Unassigned (Local)";
        var nodeId = _viewModel.SelectedNode?.NodeId ?? Guid.Empty;

        var editor = new AccountEditorWindow("Add Account", nodeName) { Owner = this };
        if (editor.ShowDialog() == true)
        {
            await RunUiActionAsync(() => _viewModel.CreateAccountAsync(new CreateAccountRequest
            {
                NodeId = nodeId,
                Email = editor.Email,
                Username = editor.Username,
                Status = "Stable"
            }));
        }
    }

    private async void InlineAction_Click(object sender, RoutedEventArgs e)
    {
        var account = GetAccountFromSender(sender);
        if (account is not null)
        {
            await RunUiActionAsync(() => _viewModel.ExecutePrimaryActionAsync(account));
        }
    }

    private void SelectAllVisible_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.SelectAllVisibleAccounts();
    }

    private void ClearSelection_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.ClearActionTargets();
    }

    private async void StartSelected_Click(object sender, RoutedEventArgs e)
    {
        await RunUiActionAsync(_viewModel.StartSelectedAccountsAsync);
    }

    private async void StopSelected_Click(object sender, RoutedEventArgs e)
    {
        await RunUiActionAsync(_viewModel.StopSelectedAccountsAsync);
    }

    private async void DeleteSelectedAccounts_Click(object sender, RoutedEventArgs e)
    {
        var confirmed = MessageBox.Show(this, "Delete the selected account targets?", "FleetManager", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirmed == MessageBoxResult.Yes)
        {
            await RunUiActionAsync(_viewModel.DeleteSelectedAccountsAsync);
        }
    }

    private async void LoginSelected_Click(object sender, RoutedEventArgs e)
    {
        await RunUiActionAsync(_viewModel.LoginSelectedAccountsAsync);
    }

    private async void StartAutoSelected_Click(object sender, RoutedEventArgs e)
    {
        await RunUiActionAsync(_viewModel.StartAutomationSelectedAccountsAsync);
    }

    private async void StopAutoSelected_Click(object sender, RoutedEventArgs e)
    {
        await RunUiActionAsync(_viewModel.StopAutomationSelectedAccountsAsync);
    }

    private async void PauseAutoSelected_Click(object sender, RoutedEventArgs e)
    {
        await RunUiActionAsync(_viewModel.PauseAutomationSelectedAccountsAsync);
    }

    private void AccountCheckBox_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.NotifySelectionChanged();
    }

    private async void ManualQueueViewBrowser_Click(object sender, RoutedEventArgs e)
    {
        var account = GetAccountFromSender(sender);
        if (account is not null)
        {
            await RunUiActionAsync(() => _viewModel.OpenRemoteViewerAsync(account));
        }
    }

    private async void ManualQueueBringToFront_Click(object sender, RoutedEventArgs e)
    {
        var account = GetAccountFromSender(sender);
        if (account is not null)
        {
            await RunUiActionAsync(() => _viewModel.BringToFrontAsync(account));
        }
    }

    private void WorkerInboxOpen_Click(object sender, RoutedEventArgs e)
    {
        var workerEvent = GetWorkerInboxEventFromSender(sender);
        if (workerEvent is null || string.IsNullOrWhiteSpace(workerEvent.ActionUrl))
        {
            MessageBox.Show(this, "This worker event does not include an actionable link.", "FleetManager", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = workerEvent.ActionUrl,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "FleetManager", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void WorkerInboxAcknowledge_Click(object sender, RoutedEventArgs e)
    {
        var workerEvent = GetWorkerInboxEventFromSender(sender);
        if (workerEvent is not null)
        {
            await RunUiActionAsync(() => _viewModel.AcknowledgeWorkerInboxEventAsync(workerEvent));
        }
    }

    private async void WorkerInboxPending_Click(object sender, RoutedEventArgs e)
    {
        await RunUiActionAsync(_viewModel.ShowPendingWorkerInboxAsync);
    }

    private async void WorkerInboxHistory_Click(object sender, RoutedEventArgs e)
    {
        await RunUiActionAsync(_viewModel.ShowWorkerInboxHistoryAsync);
    }

    private async void StartAccount_Click(object sender, RoutedEventArgs e)
    {
        var account = GetAccountFromSender(sender);
        if (account is not null)
        {
            await RunUiActionAsync(() => _viewModel.StartAccountAsync(account));
        }
    }

    private async void StopAccount_Click(object sender, RoutedEventArgs e)
    {
        var account = GetAccountFromSender(sender);
        if (account is not null)
        {
            await RunUiActionAsync(() => _viewModel.StopAccountAsync(account));
        }
    }

    private async void EditAccount_Click(object sender, RoutedEventArgs e)
    {
        var account = GetAccountFromSender(sender);
        if (account is null)
        {
            return;
        }

        var editor = new AccountEditorWindow("Edit Account", account.NodeDisplayName, account.Email, account.Username) { Owner = this };
        if (editor.ShowDialog() == true)
        {
            await RunUiActionAsync(() => _viewModel.UpdateAccountAsync(account.AccountId, new UpdateAccountRequest
            {
                Email = editor.Email,
                Username = editor.Username,
                Status = account.Status // Keep existing status
            }));
        }
    }

    private async void InjectProxies_Click(object sender, RoutedEventArgs e)
    {
        var account = GetAccountFromSender(sender);
        if (account is null)
        {
            return;
        }

        var editor = new InjectProxiesWindow(account.Email, account.ProxyCount, account.ProxyIndexLabel) { Owner = this };
        if (editor.ShowDialog() == true)
        {
            await RunUiActionAsync(() => _viewModel.InjectProxiesAsync(account, editor.RawProxies, editor.ReplaceMode));
        }
    }

    private async void RotateProxyNow_Click(object sender, RoutedEventArgs e)
    {
        var account = GetAccountFromSender(sender);
        if (account is not null)
        {
            await RunUiActionAsync(() => _viewModel.RotateProxyNowAsync(account));
        }
    }

    private async void FocusedInjectProxies_Click(object sender, RoutedEventArgs e)
    {
        var account = _viewModel.FocusedAccount ?? _viewModel.SelectedAccount;
        if (account is null)
        {
            MessageBox.Show(this, "Select an account first.", "FleetManager", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var editor = new InjectProxiesWindow(account.Email, account.ProxyCount, account.ProxyIndexLabel) { Owner = this };
        if (editor.ShowDialog() == true)
        {
            await RunUiActionAsync(() => _viewModel.InjectProxiesAsync(account, editor.RawProxies, editor.ReplaceMode));
        }
    }

    private async void FocusedRotateProxyNow_Click(object sender, RoutedEventArgs e)
    {
        var account = _viewModel.FocusedAccount ?? _viewModel.SelectedAccount;
        if (account is null)
        {
            MessageBox.Show(this, "Select an account first.", "FleetManager", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        await RunUiActionAsync(() => _viewModel.RotateProxyNowAsync(account));
    }

    private async void RemoteViewer_Click(object sender, RoutedEventArgs e)
    {
        var account = GetAccountFromSender(sender);
        if (account is null) return;

        // Dispatch OpenAssignedSession and wait for the VNC URL
        await RunUiActionAsync(() => _viewModel.OpenRemoteViewerAsync(account));

        // Open the URL directly in the user's default browser
        var vncUrl = _viewModel.GetFocusedViewerUrl();
        if (!string.IsNullOrWhiteSpace(vncUrl))
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = vncUrl,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Failed to open browser: {ex.Message}", "FleetManager", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private async void OpenLogs_Click(object sender, RoutedEventArgs e)
    {
        var account = GetAccountFromSender(sender);
        if (account is not null)
        {
            await RunUiActionAsync(() => _viewModel.OpenLogsAsync(account));
        }
    }

    private async void DeleteAccount_Click(object sender, RoutedEventArgs e)
    {
        var account = GetAccountFromSender(sender);
        if (account is null)
        {
            return;
        }

        var confirmed = MessageBox.Show(this, $"Delete account {account.Email}?", "FleetManager", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirmed == MessageBoxResult.Yes)
        {
            await RunUiActionAsync(() => _viewModel.DeleteAccountAsync(account));
        }
    }

    private void AccountRow_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGridRow row)
        {
            row.IsSelected = true;
            row.Focus();
        }
    }

    private void CopyViewerUrl_Click(object sender, RoutedEventArgs e)
    {
        var viewerUrl = _viewModel.GetFocusedViewerUrl();
        if (string.IsNullOrWhiteSpace(viewerUrl))
        {
            MessageBox.Show(this, "No viewer URL is available for the selected account.", "FleetManager", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Clipboard.SetText(viewerUrl);
    }

    private void OpenViewerUrl_Click(object sender, RoutedEventArgs e)
    {
        var viewerUrl = _viewModel.GetFocusedViewerUrl();
        if (string.IsNullOrWhiteSpace(viewerUrl))
        {
            MessageBox.Show(this, "No viewer URL is available for the selected account.", "FleetManager", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = viewerUrl,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "FleetManager", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CopyNodeId_Click(object sender, RoutedEventArgs e)
    {
        var nodeId = _viewModel.GetSelectedNodeIdText();
        if (string.IsNullOrWhiteSpace(nodeId))
        {
            MessageBox.Show(this, "No VPS is selected.", "FleetManager", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Clipboard.SetText(nodeId);
    }

    private void CopyWorkerConfig_Click(object sender, RoutedEventArgs e)
    {
        var configJson = _viewModel.GetSelectedNodeWorkerConfigJson();
        if (string.IsNullOrWhiteSpace(configJson))
        {
            MessageBox.Show(this, "No worker config is available for the selected VPS.", "FleetManager", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Clipboard.SetText(configJson);
    }

    private void CopyInstallCommand_Click(object sender, RoutedEventArgs e)
    {
        var installCommand = _viewModel.GetSelectedNodeInstallCommand();
        if (string.IsNullOrWhiteSpace(installCommand))
        {
            MessageBox.Show(this, "No install command is available for the selected VPS.", "FleetManager", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Clipboard.SetText(installCommand);
    }

    private async void DeleteVps_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedNode is null)
        {
            MessageBox.Show(this, "No VPS is selected.", "FleetManager", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirmed = MessageBox.Show(
            this,
            $"Delete VPS '{_viewModel.SelectedNode.Name}'?\n\nThis will permanently remove this node and all its associated accounts from the server.",
            "FleetManager",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirmed == MessageBoxResult.Yes)
        {
            await RunUiActionAsync(() => _viewModel.DeleteNodeAsync(_viewModel.SelectedNode.NodeId));
        }
    }

    private static AccountCardViewModel? GetAccountFromSender(object sender)
        => sender is FrameworkElement element ? element.DataContext as AccountCardViewModel : null;

    private static WorkerInboxEventViewModel? GetWorkerInboxEventFromSender(object sender)
        => sender is FrameworkElement element ? element.DataContext as WorkerInboxEventViewModel : null;

    private async Task RunUiActionAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "FleetManager", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
