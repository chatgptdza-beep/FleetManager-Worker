using System;
using System.Diagnostics;
using System.Text.Json;
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
        CompleteTakeoverButton.Content   = "Saving...";

        try
        {
            var updated = await _dataService.CompleteManualTakeoverAsync(_accountId);

            if (updated is not null)
            {
                this.Close();
            }
            else
            {
                MessageBox.Show("Failed to persist manual takeover completion. Please try again.",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                CompleteTakeoverButton.IsEnabled = true;
                CompleteTakeoverButton.Content   = "Finish Manual Takeover";
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error completing takeover: {ex.Message}",
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            CompleteTakeoverButton.IsEnabled = true;
            CompleteTakeoverButton.Content   = "Finish Manual Takeover";
        }
    }

    private async void CopyRemoteClipboard_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (BrowserWebView.CoreWebView2 is null)
            {
                MessageBox.Show("Viewer is not ready yet.", "Clipboard", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var script = @"(() => {
                const el = document.getElementById('noVNC_clipboard_text')
                    || document.querySelector('#noVNC_clipboard textarea');
                return el ? (el.value || '') : '';
            })();";

            var json = await BrowserWebView.CoreWebView2.ExecuteScriptAsync(script);
            var text = JsonSerializer.Deserialize<string>(json) ?? string.Empty;

            if (string.IsNullOrWhiteSpace(text))
            {
                MessageBox.Show("Remote clipboard is empty.", "Clipboard", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            Clipboard.SetText(text);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Copy failed: {ex.Message}", "Clipboard", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void PasteLocalClipboard_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (BrowserWebView.CoreWebView2 is null)
            {
                MessageBox.Show("Viewer is not ready yet.", "Clipboard", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!Clipboard.ContainsText())
            {
                MessageBox.Show("Local clipboard has no text.", "Clipboard", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var localText = Clipboard.GetText();
            var encodedText = JsonSerializer.Serialize(localText);
            var script = $@"(() => {{
                const el = document.getElementById('noVNC_clipboard_text')
                    || document.querySelector('#noVNC_clipboard textarea');
                if (!el) return 'NO_CLIPBOARD';
                el.value = {encodedText};
                el.dispatchEvent(new Event('input', {{ bubbles: true }}));
                el.dispatchEvent(new Event('change', {{ bubbles: true }}));
                return 'OK';
            }})();";

            var json = await BrowserWebView.CoreWebView2.ExecuteScriptAsync(script);
            var result = JsonSerializer.Deserialize<string>(json) ?? string.Empty;

            if (!string.Equals(result, "OK", StringComparison.Ordinal))
            {
                MessageBox.Show("Remote clipboard UI is not available in current viewer state.", "Clipboard", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Paste failed: {ex.Message}", "Clipboard", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
