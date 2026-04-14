using FleetManager.Desktop.Models;

namespace FleetManager.Desktop.Services;

public enum TunnelState
{
    Disconnected = 0,
    Connecting = 1,
    Connected = 2,
    Failed = 3
}

public interface ISshTunnelManager : IAsyncDisposable
{
    /// <summary>
    /// Opens an SSH local-forward tunnel: 127.0.0.1:{node.LocalPort} → {node.CurrentIp}:5000 (API).
    /// Returns true if the tunnel was established successfully.
    /// </summary>
    Task<bool> OpenTunnelAsync(DesktopManagedNodeRecord node, CancellationToken cancellationToken = default);

    /// <summary>
    /// Closes the SSH tunnel for a specific node if one is open.
    /// </summary>
    Task CloseTunnelAsync(Guid workflowNodeId);

    /// <summary>
    /// Closes all active SSH tunnels. Called during application shutdown.
    /// </summary>
    Task CloseAllAsync();

    /// <summary>
    /// Returns the current tunnel state for the given node.
    /// </summary>
    TunnelState GetTunnelState(Guid workflowNodeId);

    /// <summary>
    /// Fired whenever a tunnel's state changes (connected, failed, disconnected).
    /// </summary>
    event Action<Guid, TunnelState>? OnTunnelStateChanged;
}
