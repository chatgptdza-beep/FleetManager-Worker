using System.Threading;
using System.Threading.Tasks;
using FleetManager.Contracts.Nodes;
using FleetManager.Desktop.Models;

namespace FleetManager.Desktop.Services;

public interface ISshProvisioningService
{
    /// <summary>
    /// Attempts to connect to the node via SSH and validates that the credentials are correct.
    /// </summary>
    Task<bool> TestConnectionAsync(CreateNodeRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if the worker agent service is currently installed and running on the remote VPS.
    /// </summary>
    Task<bool> IsAgentRunningAsync(CreateNodeRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs a lightweight SSH diagnostic against a locally registered node and reports whether
    /// the agent, API, or legacy docker worker are currently alive on the remote VPS.
    /// </summary>
    Task<DesktopNodeDiagnosticResult> DiagnoseNodeAsync(CreateNodeRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs a lightweight SSH diagnostic against a locally registered node using stored credentials only.
    /// This is kept for callers that do not have a resolved connection request yet.
    /// </summary>
    Task<DesktopNodeDiagnosticResult> DiagnoseNodeAsync(DesktopManagedNodeRecord node, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes FleetManager.Api from the local workspace to the VPS and starts it as a systemd service.
    /// </summary>
    Task InstallApiAsync(CreateNodeRequest request, IProgress<string>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads a prebuilt worker bundle from GitHub and installs it on the VPS.
    /// Uses the provided backend API URL to configure the agent to point back to us.
    /// </summary>
    Task InstallAgentAsync(CreateNodeRequest request, string apiBaseUrl, IProgress<string>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the remote appsettings with the final node identity and restarts the worker service.
    /// </summary>
    Task ConfigureAgentAsync(CreateNodeRequest request, Guid nodeId, string apiBaseUrl, IProgress<string>? progress = null, CancellationToken cancellationToken = default);
}
