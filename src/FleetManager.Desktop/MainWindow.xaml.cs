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
    private readonly MainWindowViewModel _viewModel = new();

    public MainWindow()
    {
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
            await RunUiActionAsync(() => _viewModel.CreateNodeAsync(editor.Request));
        }
    }

    private async void AddAccount_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedNode is null)
        {
            await RunUiActionAsync(() => Task.FromException(new InvalidOperationException("Select a VPS tab before adding an account.")));
            return;
        }

        var editor = new AccountEditorWindow("Add Account", _viewModel.SelectedNode.NodeDisplayName, status: "Running") { Owner = this };
        if (editor.ShowDialog() == true)
        {
            await RunUiActionAsync(() => _viewModel.CreateAccountAsync(new CreateAccountRequest
            {
                NodeId = _viewModel.SelectedNode.NodeId,
                Email = editor.Email,
                Username = editor.Username,
                Status = editor.Status
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

    private async void StartAccount_Click(object sender, RoutedEventArgs e)
    {
        var account = GetAccountFromSender(sender);
        if (account is not null)
        {
            await RunUiActionAsync(() => _viewModel.StartAccountAsync(account));
        }
    }

    private async void EditAccount_Click(object sender, RoutedEventArgs e)
    {
        var account = GetAccountFromSender(sender);
        if (account is null)
        {
            return;
        }

        var editor = new AccountEditorWindow("Edit Account", account.NodeDisplayName, account.Email, account.Username, account.Status) { Owner = this };
        if (editor.ShowDialog() == true)
        {
            await RunUiActionAsync(() => _viewModel.UpdateAccountAsync(account.AccountId, new UpdateAccountRequest
            {
                Email = editor.Email,
                Username = editor.Username,
                Status = editor.Status
            }));
        }
    }

    private async void RemoteViewer_Click(object sender, RoutedEventArgs e)
    {
        var account = GetAccountFromSender(sender);
        if (account is null) return;

        // Dispatch OpenAssignedSession and wait for the VNC URL
        await RunUiActionAsync(() => _viewModel.OpenRemoteViewerAsync(account));

        // Open the inline noVNC window once the URL is available
        var vncUrl = _viewModel.GetFocusedViewerUrl();
        if (!string.IsNullOrWhiteSpace(vncUrl) && account.NodeId != Guid.Empty)
        {
            var takeoverWindow = new RemoteTakeoverWindow(
                account.AccountId,
                account.NodeId,
                vncUrl,
                _viewModel.DataService)
            {
                Owner = this
            };
            takeoverWindow.Show();
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
            await RunUiActionAsync(() => _viewModel.DeleteAccountAsync(account.AccountId));
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

    private static AccountCardViewModel? GetAccountFromSender(object sender)
        => sender is FrameworkElement element ? element.DataContext as AccountCardViewModel : null;

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
