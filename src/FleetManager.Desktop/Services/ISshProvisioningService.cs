using System.Threading;
using System.Threading.Tasks;
using FleetManager.Contracts.Nodes;

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
    /// Executes the bootstrap command to download and install the worker agent on the VPS.
    /// Uses the provided backend API URL to configure the agent to point back to us.
    /// </summary>
    Task InstallAgentAsync(CreateNodeRequest request, string apiBaseUrl, IProgress<string>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the remote appsettings with the final node identity and restarts the worker service.
    /// </summary>
    Task ConfigureAgentAsync(CreateNodeRequest request, Guid nodeId, string apiBaseUrl, IProgress<string>? progress = null, CancellationToken cancellationToken = default);
}
