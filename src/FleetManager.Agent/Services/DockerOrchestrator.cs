using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Logging;

namespace FleetManager.Agent.Services;

public class DockerOrchestrator
{
    private readonly DockerClient _client;
    private readonly ILogger<DockerOrchestrator> _logger;

    public DockerOrchestrator(ILogger<DockerOrchestrator> logger)
    {
        _logger = logger;
        // In Linux, Docker socket is typically at unix:///var/run/docker.sock
        // In Windows, npipe://./pipe/docker_engine
        var dockerUri = OperatingSystem.IsWindows() 
            ? "npipe://./pipe/docker_engine" 
            : "unix:///var/run/docker.sock";
            
        _client = new DockerClientConfiguration(new Uri(dockerUri)).CreateClient();
    }

    public async Task<string> StartBrowserAsync(Guid accountId, string proxyConfig, int vncPort, CancellationToken cancellationToken)
    {
        string containerName = $"fleet-browser-{accountId}";

        try
        {
            // Remove if already exists
            await RemoveBrowserAsync(accountId, cancellationToken);

            _logger.LogInformation("Creating container {ContainerName} with VNC on port {Port}", containerName, vncPort);

            var response = await _client.Containers.CreateContainerAsync(new CreateContainerParameters
            {
                Image = "fleet-browser", // The image built from Dockerfile.browser
                Name = containerName,
                HostConfig = new HostConfig
                {
                    PortBindings = new Dictionary<string, IList<PortBinding>>
                    {
                        { "6080/tcp", new List<PortBinding> { new PortBinding { HostPort = vncPort.ToString() } } }
                    },
                    Memory = 512 * 1024 * 1024, // 512m
                    ShmSize = 2L * 1024 * 1024 * 1024, // 2g for browser stability
                    Binds = new List<string>
                    {
                        $"/opt/fleetmanager/profiles/{accountId}:/data/profile",
                        $"/opt/fleetmanager/scripts:/app/scripts"
                    }
                },
                Cmd = new List<string> { "scripts/navigate.js", $"--proxy={proxyConfig}" },
                Env = new List<string> { "NODE_ENV=production" }
            }, cancellationToken);

            await _client.Containers.StartContainerAsync(response.ID, new ContainerStartParameters(), cancellationToken);
            return response.ID;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start browser container {ContainerName}", containerName);
            throw;
        }
    }

    public async Task RemoveBrowserAsync(Guid accountId, CancellationToken cancellationToken)
    {
        string containerName = $"fleet-browser-{accountId}";
        try
        {
            var containers = await _client.Containers.ListContainersAsync(new ContainersListParameters { All = true }, cancellationToken);
            var container = containers.FirstOrDefault(c => c.Names.Contains($"/{containerName}"));

            if (container != null)
            {
                await _client.Containers.StopContainerAsync(container.ID, new ContainerStopParameters { WaitBeforeKillSeconds = 2 }, cancellationToken);
                await _client.Containers.RemoveContainerAsync(container.ID, new ContainerRemoveParameters { Force = true }, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not remove container {ContainerName}", containerName);
        }
    }
}
