using System;
using System.Windows;
using System.Windows.Media;

namespace FleetManager.Desktop;

public partial class InstallConsoleWindow : Window
{
    public InstallConsoleWindow()
    {
        InitializeComponent();
        UpdateProgress(0);
    }

    public void AppendLog(string message)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => AppendLog(message));
            return;
        }

        if (TryParseProgress(message, out var pct))
        {
            UpdateProgress(pct);
            return;
        }

        if (!string.IsNullOrWhiteSpace(message)
            && !message.StartsWith("  >", StringComparison.Ordinal)
            && !message.StartsWith("  [err]", StringComparison.Ordinal))
        {
            StatusText.Text = message;
        }

        LogOutput.Text += message + Environment.NewLine;
        LogScroller.ScrollToEnd();
    }

    public void MarkSuccess()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(MarkSuccess);
            return;
        }

        UpdateProgress(100);
        HeaderText.Text = "Agent Installed Successfully";
        HeaderText.Foreground = new SolidColorBrush(Color.FromRgb(0x47, 0xC1, 0xA8));
        StatusText.Text = "Installation complete.";
        CloseBtn.IsEnabled = true;
    }

    public void MarkFailed(string error)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => MarkFailed(error));
            return;
        }

        UpdateProgress(100);
        HeaderText.Text = "Installation Failed";
        HeaderText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B));
        InstallProgress.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B));
        StatusText.Text = $"Error: {error}";
        CloseBtn.IsEnabled = true;
        AppendLog($"\n*** FAILED: {error} ***");
    }

    private void UpdateProgress(int pct)
    {
        var clamped = Math.Clamp(pct, 0, 100);
        InstallProgress.IsIndeterminate = false;
        InstallProgress.Value = clamped;
        ProgressPercentText.Text = $"{clamped}%";
        HeaderText.Text = $"Installing FleetManager Agent... {clamped}%";
    }

    private static bool TryParseProgress(string message, out int pct)
    {
        pct = 0;
        if (string.IsNullOrWhiteSpace(message) || message[0] != '%')
        {
            return false;
        }

        var digitCount = 0;
        for (var index = 1; index < message.Length && char.IsDigit(message[index]); index++)
        {
            digitCount++;
        }

        return digitCount > 0
            && int.TryParse(message.AsSpan(1, digitCount), out pct);
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
