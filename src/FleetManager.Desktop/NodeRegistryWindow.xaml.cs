using System.Windows;
using System.Windows.Input;
using FleetManager.Desktop.Models;
using FleetManager.Desktop.Services;

namespace FleetManager.Desktop;

public partial class NodeRegistryWindow : Window
{
    private readonly IDesktopNodeRegistry _nodeRegistry;
    private readonly ISshProvisioningService _sshProvisioning;
    private readonly ISshTunnelManager _tunnelManager;
    private List<NodeRegistryRow> _rows = new();

    public NodeRegistryWindow(
        IDesktopNodeRegistry nodeRegistry,
        ISshProvisioningService sshProvisioning,
        ISshTunnelManager tunnelManager)
    {
        _nodeRegistry = nodeRegistry;
        _sshProvisioning = sshProvisioning;
        _tunnelManager = tunnelManager;
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        await RefreshGridAsync();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
        => await RefreshGridAsync();

    private async void DiagnoseAll_Click(object sender, RoutedEventArgs e)
    {
        Mouse.OverrideCursor = Cursors.Wait;
        try
        {
            var nodes = await _nodeRegistry.GetNodesAsync();
            foreach (var node in nodes)
            {
                try
                {
                    var diagnostic = _nodeRegistry.HasUsableCredentials(node)
                        ? await _sshProvisioning.DiagnoseNodeAsync(_nodeRegistry.BuildConnectionRequest(node))
                        : await _sshProvisioning.DiagnoseNodeAsync(node);
                    await _nodeRegistry.UpdateNodeHealthAsync(
                        node.WorkflowNodeId,
                        diagnostic.IsSshReachable ? DesktopManagedNodeStatus.RemoteReachable : DesktopManagedNodeStatus.ActionRequired,
                        diagnostic.Detail,
                        DateTime.UtcNow,
                        sshSuccessAtUtc: diagnostic.IsSshReachable ? DateTime.UtcNow : null);
                }
                catch (Exception ex)
                {
                    await _nodeRegistry.UpdateNodeHealthAsync(
                        node.WorkflowNodeId,
                        DesktopManagedNodeStatus.Error,
                        $"Diagnostic failed: {ex.Message}",
                        DateTime.UtcNow);
                }
            }

            await RefreshGridAsync();
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    private async void Edit_Click(object sender, RoutedEventArgs e)
    {
        var row = GetSelectedRow();
        if (row is null) return;

        var node = await _nodeRegistry.GetByWorkflowNodeIdAsync(row.WorkflowNodeId);
        if (node is null)
        {
            MessageBox.Show(this, "Node not found in registry.", "FleetManager", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var editor = new EditNodeWindow(node, _sshProvisioning) { Owner = this };
        if (editor.ShowDialog() == true)
        {
            try
            {
                await _nodeRegistry.UpdateNodeConnectionAsync(
                    node.WorkflowNodeId,
                    editor.ResultName,
                    editor.ResultIp,
                    editor.ResultSshPort,
                    editor.ResultSshUsername,
                    editor.ResultSshPassword,
                    editor.ResultSshPrivateKey,
                    editor.ResultAuthType,
                    editor.ResultControlPort,
                    editor.ResultRegion);

                await RefreshGridAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Failed to save: {ex.Message}", "FleetManager", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private async void Diagnose_Click(object sender, RoutedEventArgs e)
    {
        var row = GetSelectedRow();
        if (row is null) return;

        var node = await _nodeRegistry.GetByWorkflowNodeIdAsync(row.WorkflowNodeId);
        if (node is null) return;

        Mouse.OverrideCursor = Cursors.Wait;
        try
        {
            var diagnostic = _nodeRegistry.HasUsableCredentials(node)
                ? await _sshProvisioning.DiagnoseNodeAsync(_nodeRegistry.BuildConnectionRequest(node))
                : await _sshProvisioning.DiagnoseNodeAsync(node);
            await _nodeRegistry.UpdateNodeHealthAsync(
                node.WorkflowNodeId,
                diagnostic.IsSshReachable ? DesktopManagedNodeStatus.RemoteReachable : DesktopManagedNodeStatus.ActionRequired,
                diagnostic.Detail,
                DateTime.UtcNow,
                sshSuccessAtUtc: diagnostic.IsSshReachable ? DateTime.UtcNow : null);

            await RefreshGridAsync();

            MessageBox.Show(this, diagnostic.Detail, "Diagnostic Result", MessageBoxButton.OK,
                diagnostic.IsSshReachable ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Diagnostic error: {ex.Message}", "FleetManager", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    private async void ToggleTunnel_Click(object sender, RoutedEventArgs e)
    {
        var row = GetSelectedRow();
        if (row is null) return;

        var node = await _nodeRegistry.GetByWorkflowNodeIdAsync(row.WorkflowNodeId);
        if (node is null) return;

        Mouse.OverrideCursor = Cursors.Wait;
        try
        {
            var currentState = _tunnelManager.GetTunnelState(node.WorkflowNodeId);
            if (currentState == TunnelState.Connected || currentState == TunnelState.Connecting)
            {
                await _tunnelManager.CloseTunnelAsync(node.WorkflowNodeId);
            }
            else
            {
                await _tunnelManager.OpenTunnelAsync(node);
            }

            await RefreshGridAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Tunnel error: {ex.Message}", "FleetManager", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    private async void Remove_Click(object sender, RoutedEventArgs e)
    {
        var row = GetSelectedRow();
        if (row is null) return;

        var confirmed = MessageBox.Show(
            this,
            $"Remove node '{row.Name}' from the local registry?\n\nThis does NOT delete the VPS from the remote API.",
            "FleetManager",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirmed != MessageBoxResult.Yes) return;

        await _tunnelManager.CloseTunnelAsync(row.WorkflowNodeId);
        await _nodeRegistry.RemoveByWorkflowNodeIdAsync(row.WorkflowNodeId);
        await RefreshGridAsync();
    }

    private void NodesGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        Edit_Click(sender, e);
    }

    private async Task RefreshGridAsync()
    {
        var nodes = await _nodeRegistry.GetNodesAsync();
        _rows = nodes.Select(node => new NodeRegistryRow
        {
            WorkflowNodeId = node.WorkflowNodeId,
            RemoteNodeId = node.RemoteNodeId,
            Name = node.Name,
            CurrentIp = node.CurrentIp,
            SshPort = node.SshPort,
            SshUsername = node.SshUsername,
            LocalPort = node.LocalPort,
            ControlPort = node.ControlPort,
            Status = node.Status,
            StatusMessage = node.StatusMessage ?? string.Empty,
            LastHealthCheckAtUtc = node.LastHealthCheckAtUtc,
            TunnelState = _tunnelManager.GetTunnelState(node.WorkflowNodeId)
        }).ToList();

        NodesGrid.ItemsSource = _rows;
        NodeCountLabel.Text = $"{_rows.Count} node(s) registered locally";
    }

    private NodeRegistryRow? GetSelectedRow()
    {
        var row = NodesGrid.SelectedItem as NodeRegistryRow;
        if (row is null)
        {
            MessageBox.Show(this, "Select a node first.", "FleetManager", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        return row;
    }

    /// <summary>
    /// Lightweight display model for the DataGrid — not persisted.
    /// </summary>
    internal sealed class NodeRegistryRow
    {
        public Guid WorkflowNodeId { get; init; }
        public Guid? RemoteNodeId { get; init; }
        public string Name { get; init; } = string.Empty;
        public string CurrentIp { get; init; } = string.Empty;
        public int SshPort { get; init; }
        public string SshUsername { get; init; } = string.Empty;
        public int LocalPort { get; init; }
        public int ControlPort { get; init; }
        public DesktopManagedNodeStatus Status { get; init; }
        public string StatusMessage { get; init; } = string.Empty;
        public DateTime? LastHealthCheckAtUtc { get; init; }
        public TunnelState TunnelState { get; init; }

        // Computed display properties
        public string SshSummary => $"{SshUsername}@:{SshPort}";
        public string StatusLabel => Status.ToString();
        public string TunnelLabel => TunnelState switch
        {
            TunnelState.Connected => "● Active",
            TunnelState.Connecting => "◌ Opening…",
            TunnelState.Failed => "✗ Failed",
            _ => "○ Off"
        };
        public string LastCheckLabel => LastHealthCheckAtUtc.HasValue
            ? LastHealthCheckAtUtc.Value.ToLocalTime().ToString("HH:mm:ss")
            : "Never";
    }
}
