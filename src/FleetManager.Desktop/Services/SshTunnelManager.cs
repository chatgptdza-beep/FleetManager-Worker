using System.Collections.Concurrent;
using System.IO;
using System.Text;
using FleetManager.Desktop.Models;
using Renci.SshNet;

namespace FleetManager.Desktop.Services;

/// <summary>
/// Manages SSH local port-forward tunnels for each registered VPS node.
/// For each node, it forwards 127.0.0.1:{LocalPort} → {VPS}:5000 (the remote FleetManager.Api).
/// The tunnel auto-reconnects if the SSH session drops.
/// </summary>
public sealed class SshTunnelManager : ISshTunnelManager
{
    private const int RemoteApiPort = 5000;
    private const int ReconnectDelayMs = 5_000;

    private readonly ConcurrentDictionary<Guid, TunnelEntry> _tunnels = new();
    private bool _disposed;

    public event Action<Guid, TunnelState>? OnTunnelStateChanged;

    public async Task<bool> OpenTunnelAsync(DesktopManagedNodeRecord node, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (node.LocalPort <= 0)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(node.CurrentIp))
        {
            return false;
        }

        // If there's already a tunnel for this node, close it first
        if (_tunnels.ContainsKey(node.WorkflowNodeId))
        {
            await CloseTunnelAsync(node.WorkflowNodeId);
        }

        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var entry = new TunnelEntry
        {
            WorkflowNodeId = node.WorkflowNodeId,
            State = TunnelState.Connecting,
            LifetimeCts = cts
        };

        _tunnels[node.WorkflowNodeId] = entry;
        RaiseStateChanged(node.WorkflowNodeId, TunnelState.Connecting);

        try
        {
            var connectionInfo = BuildConnectionInfo(node);
            var sshClient = new SshClient(connectionInfo);

            await Task.Run(() => sshClient.Connect(), cancellationToken);

            if (!sshClient.IsConnected)
            {
                entry.State = TunnelState.Failed;
                RaiseStateChanged(node.WorkflowNodeId, TunnelState.Failed);
                return false;
            }

            var forwardedPort = new ForwardedPortLocal("127.0.0.1", (uint)node.LocalPort, "127.0.0.1", RemoteApiPort);
            sshClient.AddForwardedPort(forwardedPort);
            forwardedPort.Start();

            entry.Client = sshClient;
            entry.Port = forwardedPort;
            entry.State = TunnelState.Connected;

            RaiseStateChanged(node.WorkflowNodeId, TunnelState.Connected);

            // Start background reconnection monitor
            entry.ReconnectTask = Task.Run(() => MonitorTunnelAsync(entry, node), cts.Token);

            return true;
        }
        catch
        {
            entry.State = TunnelState.Failed;
            RaiseStateChanged(node.WorkflowNodeId, TunnelState.Failed);
            return false;
        }
    }

    public Task CloseTunnelAsync(Guid workflowNodeId)
    {
        if (_tunnels.TryRemove(workflowNodeId, out var entry))
        {
            DisposeTunnelEntry(entry);
            RaiseStateChanged(workflowNodeId, TunnelState.Disconnected);
        }

        return Task.CompletedTask;
    }

    public async Task CloseAllAsync()
    {
        var ids = _tunnels.Keys.ToList();
        foreach (var id in ids)
        {
            await CloseTunnelAsync(id);
        }
    }

    public TunnelState GetTunnelState(Guid workflowNodeId)
    {
        return _tunnels.TryGetValue(workflowNodeId, out var entry)
            ? entry.State
            : TunnelState.Disconnected;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await CloseAllAsync();
    }

    /// <summary>
    /// Background loop that watches the SSH session and attempts reconnection if it drops.
    /// </summary>
    private async Task MonitorTunnelAsync(TunnelEntry entry, DesktopManagedNodeRecord node)
    {
        while (!entry.LifetimeCts.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(ReconnectDelayMs, entry.LifetimeCts.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            // Check if tunnel is still healthy
            if (entry.Client?.IsConnected == true && entry.Port?.IsStarted == true)
            {
                continue;
            }

            // Tunnel dropped — attempt reconnection
            entry.State = TunnelState.Connecting;
            RaiseStateChanged(entry.WorkflowNodeId, TunnelState.Connecting);

            try
            {
                CleanupClientAndPort(entry);

                var connectionInfo = BuildConnectionInfo(node);
                var sshClient = new SshClient(connectionInfo);
                sshClient.Connect();

                if (!sshClient.IsConnected)
                {
                    entry.State = TunnelState.Failed;
                    RaiseStateChanged(entry.WorkflowNodeId, TunnelState.Failed);
                    continue;
                }

                var forwardedPort = new ForwardedPortLocal("127.0.0.1", (uint)node.LocalPort, "127.0.0.1", RemoteApiPort);
                sshClient.AddForwardedPort(forwardedPort);
                forwardedPort.Start();

                entry.Client = sshClient;
                entry.Port = forwardedPort;
                entry.State = TunnelState.Connected;
                RaiseStateChanged(entry.WorkflowNodeId, TunnelState.Connected);
            }
            catch
            {
                entry.State = TunnelState.Failed;
                RaiseStateChanged(entry.WorkflowNodeId, TunnelState.Failed);
            }
        }
    }

    private static ConnectionInfo BuildConnectionInfo(DesktopManagedNodeRecord node)
    {
        var authMethods = new List<AuthenticationMethod>();

        var decryptedPassword = DesktopCredentialProtector.Unprotect(node.EncryptedSshPassword);
        var decryptedPrivateKey = DesktopCredentialProtector.Unprotect(node.EncryptedSshPrivateKey);

        if (!string.IsNullOrWhiteSpace(decryptedPrivateKey))
        {
            using var memoryStream = new MemoryStream(Encoding.UTF8.GetBytes(decryptedPrivateKey));
            var privateKeyFile = new PrivateKeyFile(memoryStream);
            authMethods.Add(new PrivateKeyAuthenticationMethod(node.SshUsername, privateKeyFile));
        }

        if (!string.IsNullOrWhiteSpace(decryptedPassword))
        {
            authMethods.Add(new PasswordAuthenticationMethod(node.SshUsername, decryptedPassword));
        }

        if (authMethods.Count == 0)
        {
            throw new InvalidOperationException(
                $"Node '{node.Name}' has no SSH credentials stored. Edit the node in the registry to add credentials.");
        }

        return new ConnectionInfo(
            node.CurrentIp,
            node.SshPort,
            node.SshUsername,
            authMethods.ToArray())
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
    }

    private static void CleanupClientAndPort(TunnelEntry entry)
    {
        try
        {
            if (entry.Port is { IsStarted: true })
            {
                entry.Port.Stop();
            }
        }
        catch { /* best-effort */ }

        try
        {
            if (entry.Client is { IsConnected: true })
            {
                entry.Client.Disconnect();
            }

            entry.Client?.Dispose();
        }
        catch { /* best-effort */ }

        entry.Client = null;
        entry.Port = null;
    }

    private static void DisposeTunnelEntry(TunnelEntry entry)
    {
        try { entry.LifetimeCts.Cancel(); } catch { }
        CleanupClientAndPort(entry);
        try { entry.LifetimeCts.Dispose(); } catch { }
    }

    private void RaiseStateChanged(Guid workflowNodeId, TunnelState state)
    {
        try
        {
            OnTunnelStateChanged?.Invoke(workflowNodeId, state);
        }
        catch { /* listener errors must not crash the tunnel manager */ }
    }

    private sealed class TunnelEntry
    {
        public Guid WorkflowNodeId { get; init; }
        public TunnelState State { get; set; }
        public SshClient? Client { get; set; }
        public ForwardedPortLocal? Port { get; set; }
        public CancellationTokenSource LifetimeCts { get; init; } = new();
        public Task? ReconnectTask { get; set; }
    }
}
