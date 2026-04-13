using System;
using System.Windows;
using System.Windows.Media;

namespace FleetManager.Desktop;

public partial class InstallConsoleWindow : Window
{
    public InstallConsoleWindow()
    {
        InitializeComponent();
    }

    public void AppendLog(string message)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => AppendLog(message));
            return;
        }

        // Messages starting with %XX are percentage updates
        if (message.Length >= 3 && message[0] == '%' && int.TryParse(message.AsSpan(1, 2), out var pct))
        {
            InstallProgress.Value = pct;
            HeaderText.Text = $"Installing FleetManager Agent... {pct}%";
            return;
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

        HeaderText.Text = "Agent Installed Successfully";
        HeaderText.Foreground = new SolidColorBrush(Color.FromRgb(0x47, 0xC1, 0xA8));
        InstallProgress.IsIndeterminate = false;
        InstallProgress.Value = 100;
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

        HeaderText.Text = "Installation Failed";
        HeaderText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B));
        InstallProgress.IsIndeterminate = false;
        InstallProgress.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B));
        InstallProgress.Value = 100;
        StatusText.Text = $"Error: {error}";
        CloseBtn.IsEnabled = true;
        AppendLog($"\n*** FAILED: {error} ***");
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
