using System.IO;
using System.Windows;
using System.Windows.Input;
using FleetManager.Contracts.Nodes;
using FleetManager.Desktop.Models;
using FleetManager.Desktop.Services;
using Microsoft.Win32;

namespace FleetManager.Desktop;

public partial class EditNodeWindow : Window
{
    private readonly ISshProvisioningService _sshProvisioning;
    private readonly DesktopManagedNodeRecord _source;
    private string? _loadedPrivateKey;

    public EditNodeWindow(DesktopManagedNodeRecord node, ISshProvisioningService sshProvisioning)
    {
        _sshProvisioning = sshProvisioning;
        _source = node;
        InitializeComponent();
        PopulateFields();
    }

    /// <summary>
    /// The final validated values the caller should use to persist the update.
    /// </summary>
    public string ResultName { get; private set; } = string.Empty;
    public string ResultIp { get; private set; } = string.Empty;
    public int ResultSshPort { get; private set; }
    public string ResultSshUsername { get; private set; } = string.Empty;
    public string? ResultSshPassword { get; private set; }
    public string? ResultSshPrivateKey { get; private set; }
    public string ResultAuthType { get; private set; } = "Password";
    public int ResultControlPort { get; private set; }
    public string? ResultRegion { get; private set; }

    private void PopulateFields()
    {
        WorkflowIdLabel.Text = $"WF: {_source.WorkflowNodeId:N}";
        StatusLabel.Text = _source.Status.ToString();

        NameTextBox.Text = _source.Name;
        IpAddressTextBox.Text = _source.CurrentIp;
        SshPortTextBox.Text = _source.SshPort.ToString();
        ControlPortTextBox.Text = _source.ControlPort.ToString();
        SshUsernameTextBox.Text = _source.SshUsername;
        RegionTextBox.Text = _source.Region ?? string.Empty;

        // Existing password is shown as placeholder — we never display the actual value.
        SshPasswordTextBox.Text = _source.HasStoredCredentials
            ? string.Empty
            : string.Empty;

        if (!string.IsNullOrWhiteSpace(_source.EncryptedSshPrivateKey))
        {
            SshPrivateKeyTextBox.Text = "(stored — import a new key to replace)";
            _loadedPrivateKey = DesktopCredentialProtector.Unprotect(_source.EncryptedSshPrivateKey);
        }
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
                _loadedPrivateKey = File.ReadAllText(dialog.FileName);
                SshPrivateKeyTextBox.Text = Path.GetFileName(dialog.FileName);
                MessageBox.Show(this, "SSH Key imported successfully.", "FleetManager", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Failed to read key file: {ex.Message}", "FleetManager", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private async void TestConnection_Click(object sender, RoutedEventArgs e)
    {
        if (!TryValidateFields(out var request))
        {
            return;
        }

        TestResultLabel.Text = "Testing SSH connection…";
        TestResultLabel.Foreground = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#B9C4CD")!);

        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            var success = await _sshProvisioning.TestConnectionAsync(request);

            if (success)
            {
                TestResultLabel.Text = "✓ SSH connection succeeded.";
                TestResultLabel.Foreground = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#84F0DA")!);
            }
            else
            {
                TestResultLabel.Text = "✗ SSH connection failed — check credentials.";
                TestResultLabel.Foreground = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FFB1B1")!);
            }
        }
        catch (Exception ex)
        {
            TestResultLabel.Text = $"✗ Error: {ex.Message}";
            TestResultLabel.Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FFB1B1")!);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!TryValidateFields(out _))
        {
            return;
        }

        ResultName = NameTextBox.Text.Trim();
        ResultIp = IpAddressTextBox.Text.Trim();
        ResultSshPort = int.Parse(SshPortTextBox.Text);
        ResultSshUsername = SshUsernameTextBox.Text.Trim();
        ResultControlPort = int.Parse(ControlPortTextBox.Text);
        ResultRegion = string.IsNullOrWhiteSpace(RegionTextBox.Text) ? null : RegionTextBox.Text.Trim();

        // Password: if the user typed something, use it. Otherwise keep the original.
        ResultSshPassword = !string.IsNullOrWhiteSpace(SshPasswordTextBox.Text)
            ? SshPasswordTextBox.Text.Trim()
            : DesktopCredentialProtector.Unprotect(_source.EncryptedSshPassword);

        // Private key: if a new key was imported, use it. Otherwise keep the original.
        ResultSshPrivateKey = _loadedPrivateKey;

        ResultAuthType = !string.IsNullOrWhiteSpace(ResultSshPrivateKey) ? "SshKey" : "Password";

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
        => DialogResult = false;

    private bool TryValidateFields(out CreateNodeRequest? request)
    {
        request = null;

        if (string.IsNullOrWhiteSpace(NameTextBox.Text))
        {
            MessageBox.Show(this, "Name is required.", "FleetManager", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (string.IsNullOrWhiteSpace(IpAddressTextBox.Text))
        {
            MessageBox.Show(this, "IP address is required.", "FleetManager", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (!int.TryParse(SshPortTextBox.Text, out var sshPort) || sshPort <= 0)
        {
            MessageBox.Show(this, "SSH port must be a valid positive number.", "FleetManager", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (!int.TryParse(ControlPortTextBox.Text, out var controlPort) || controlPort <= 0)
        {
            MessageBox.Show(this, "Control port must be a valid positive number.", "FleetManager", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        var password = !string.IsNullOrWhiteSpace(SshPasswordTextBox.Text)
            ? SshPasswordTextBox.Text.Trim()
            : DesktopCredentialProtector.Unprotect(_source.EncryptedSshPassword);

        var privateKey = _loadedPrivateKey;

        request = new CreateNodeRequest
        {
            Name = NameTextBox.Text.Trim(),
            IpAddress = IpAddressTextBox.Text.Trim(),
            SshPort = sshPort,
            ControlPort = controlPort,
            SshUsername = SshUsernameTextBox.Text.Trim(),
            SshPassword = password,
            SshPrivateKey = privateKey,
            AuthType = !string.IsNullOrWhiteSpace(privateKey) ? "SshKey" : "Password",
            OsType = _source.OsType,
            Region = string.IsNullOrWhiteSpace(RegionTextBox.Text) ? null : RegionTextBox.Text.Trim()
        };

        return true;
    }
}
