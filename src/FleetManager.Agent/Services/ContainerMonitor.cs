using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Docker.DotNet;
using Docker.DotNet.Models;
using FleetManager.Agent.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FleetManager.Agent.Services;

public class ContainerMonitor : BackgroundService
{
    private readonly DockerClient _client;
    private readonly ILogger<ContainerMonitor> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AgentOptions _settings;
    private readonly DockerOrchestrator _orchestrator;

    public ContainerMonitor(
        ILogger<ContainerMonitor> logger, 
        IHttpClientFactory httpClientFactory, 
        IOptions<AgentOptions> options,
        DockerOrchestrator orchestrator)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _settings = options.Value;
        _orchestrator = orchestrator;

        var dockerUri = OperatingSystem.IsWindows() 
            ? "npipe://./pipe/docker_engine" 
            : "unix:///var/run/docker.sock";
            
        _client = new DockerClientConfiguration(new Uri(dockerUri)).CreateClient();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var containers = await _client.Containers.ListContainersAsync(new ContainersListParameters { All = true }, stoppingToken);
                var fleetContainers = containers.Where(c => c.Names.Any(n => n.StartsWith("/fleet-browser-"))).ToList();

                foreach (var container in fleetContainers)
                {
                    await CheckContainerLogsAsync(container, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to monitor containers.");
            }

            await Task.Delay(5000, stoppingToken);
        }
    }

    private async Task CheckContainerLogsAsync(ContainerListResponse container, CancellationToken stoppingToken)
    {
        // Extract AccountId from container name expected format: /fleet-browser-{accountId}
        var name = container.Names.First();
        var idStr = name.Replace("/fleet-browser-", "");
        if (!Guid.TryParse(idStr, out Guid accountId)) return;

        try
        {
            using var logStream = await _client.Containers.GetContainerLogsAsync(
                container.ID, false,
                new ContainerLogsParameters { ShowStdout = true, ShowStderr = true, Tail = "20" },
                stoppingToken);

            var logs = await logStream.ReadOutputToEndAsync(stoppingToken);

            if (logs.stdout.Contains("ERROR:429") || logs.stderr.Contains("ERROR:429") || 
                logs.stdout.Contains("ERROR:403") || logs.stderr.Contains("ERROR:403"))
            {
                _logger.LogWarning("Rate limit detected in container {Name}. Triggering proxy rotation.", name);
                await TriggerProxyRotationAsync(accountId, stoppingToken);
                await _orchestrator.RemoveBrowserAsync(accountId, stoppingToken); // Kill container
            }
            else if (logs.stdout.Contains("SLOT_FOUND") || logs.stderr.Contains("SLOT_FOUND"))
            {
                _logger.LogInformation("Slot found or manual intervention required in container {Name}.", name);
                await RequestManualTakeoverAsync(accountId, stoppingToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogTrace(ex, "Could not fetch logs for container {Name}.", name);
        }
    }

    private async Task TriggerProxyRotationAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient();
        client.BaseAddress = new Uri(_settings.BackendBaseUrl);
        client.DefaultRequestHeaders.Add("X-Api-Key", _settings.ApiKey ?? "MASTER-KEY-12345");

        var payload = new { Reason = "429 Rate Limit" };
        var response = await client.PostAsJsonAsync($"/api/accounts/{accountId}/proxies/rotate", payload, cancellationToken);
        
        if (response.IsSuccessStatusCode)
        {
            _logger.LogInformation("Proxy rotated successfully for account {AccountId}.", accountId);
            // It will be restarted by the API/Operations hub sending a new StartBrowser command
        }
    }

    private async Task RequestManualTakeoverAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient();
        client.BaseAddress = new Uri(_settings.BackendBaseUrl);
        client.DefaultRequestHeaders.Add("X-Api-Key", _settings.ApiKey ?? "MASTER-KEY-12345");

        // Construct VNC URL pointing to backend
        string vncUrl = $"http://{_settings.NodeIpAddress}:{_settings.ControlPort}/vnc.html";
        var payload = new { VncUrl = vncUrl };

        await client.PostAsJsonAsync($"/api/accounts/{accountId}/proxies/takeover-request", payload, cancellationToken);
    }
}
