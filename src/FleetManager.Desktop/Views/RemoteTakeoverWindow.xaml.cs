using System;
using System.Windows;
using FleetManager.Contracts.Nodes;
using FleetManager.Desktop.Services;

namespace FleetManager.Desktop.Views;

public partial class RemoteTakeoverWindow : Window
{
    private readonly Guid _accountId;
    private readonly Guid _nodeId;
    private readonly IDashboardDataService _dataService;

    public RemoteTakeoverWindow(Guid accountId, Guid nodeId, string vncUrl, IDashboardDataService dataService)
    {
        InitializeComponent();
        _accountId  = accountId;
        _nodeId     = nodeId;
        _dataService = dataService;
        UrlTextBlock.Text = $"Connected to VNC: {vncUrl}";
        InitializeAsync(vncUrl);
    }

    private async void InitializeAsync(string url)
    {
        await BrowserWebView.EnsureCoreWebView2Async(null);
        BrowserWebView.CoreWebView2.Navigate(url);
    }

    private async void CompleteTakeover_Click(object sender, RoutedEventArgs e)
    {
        CompleteTakeoverButton.IsEnabled = false;
        CompleteTakeoverButton.Content   = "Sending...";

        try
        {
            var commandId = await _dataService.DispatchNodeCommandAsync(
                _nodeId,
                new DispatchNodeCommandRequest
                {
                    CommandType = "TakeoverComplete",
                    PayloadJson = $"{{\"accountId\":\"{_accountId}\"}}"
                });

            if (commandId.HasValue)
            {
                this.Close();
            }
            else
            {
                MessageBox.Show("Failed to send TakeoverComplete command. Please try again.",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                CompleteTakeoverButton.IsEnabled = true;
                CompleteTakeoverButton.Content   = "Resume Automation (Takeover Complete)";
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error completing takeover: {ex.Message}",
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            CompleteTakeoverButton.IsEnabled = true;
            CompleteTakeoverButton.Content   = "Resume Automation (Takeover Complete)";
        }
    }
}

