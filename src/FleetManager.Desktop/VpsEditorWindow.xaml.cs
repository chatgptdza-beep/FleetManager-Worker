using System.Windows;
using Microsoft.Win32;
using FleetManager.Contracts.Nodes;
using System.IO;

namespace FleetManager.Desktop;

public partial class VpsEditorWindow : Window
{
    public VpsEditorWindow()
    {
        InitializeComponent();
    }

    public CreateNodeRequest Request { get; private set; } = new();

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameTextBox.Text))
        {
            MessageBox.Show(this, "Name is required.", "FleetManager", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(IpAddressTextBox.Text))
        {
            MessageBox.Show(this, "IP address is required.", "FleetManager", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(SshPortTextBox.Text, out var sshPort) || sshPort <= 0)
        {
            MessageBox.Show(this, "SSH port must be a valid positive number.", "FleetManager", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(ControlPortTextBox.Text, out var controlPort) || controlPort <= 0)
        {
            MessageBox.Show(this, "Control port must be a valid positive number.", "FleetManager", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Request = new CreateNodeRequest
        {
            Name = NameTextBox.Text.Trim(),
            IpAddress = IpAddressTextBox.Text.Trim(),
            SshPort = sshPort,
            ControlPort = controlPort,
            SshUsername = SshUsernameTextBox.Text.Trim(),
            SshPassword = SshPasswordTextBox.Text?.Trim(),
            SshPrivateKey = SshPrivateKeyTextBox.Text?.Trim(),
            AuthType = string.IsNullOrWhiteSpace(SshPrivateKeyTextBox.Text) ? "Password" : "SshKey",
            OsType = "Ubuntu",
            Region = null
        };

        DialogResult = true;
    }

    private void ImportKey_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Private Key files (*.pem;*.key;*.ppk)|*.pem;*.key;*.ppk|All files (*.*)|*.*",
            Title = "Select SSH Private Key File"
        };

        if (dialog.ShowDialog(this) == true)
        {
            try
            {
                string keyContent = File.ReadAllText(dialog.FileName);
                SshPrivateKeyTextBox.Text = keyContent;
                MessageBox.Show(this, "SSH Key imported successfully.", "FleetManager", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(this, $"Failed to read key file: {ex.Message}", "FleetManager", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
        => DialogResult = false;
}
