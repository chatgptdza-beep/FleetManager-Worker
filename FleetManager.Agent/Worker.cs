using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace FleetManager.Agent;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly HttpClient _httpClient;
    private readonly AgentSettings _settings;

    public Worker(ILogger<Worker> logger, HttpClient httpClient, IOptions<AgentSettings> settings)
    {
        _logger = logger;
        _httpClient = httpClient;
        _settings = settings.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Fleet Manager Agent starting. NodeId={NodeId}, Version={Version}, Backend={BackendBaseUrl}",
            _settings.NodeId,
            _settings.AgentVersion,
            _settings.BackendBaseUrl);

        while (!stoppingToken.IsCancellationRequested)
        {
            await SendHeartbeatAsync(stoppingToken);
            try
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(_settings.HeartbeatIntervalSeconds),
                    stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Graceful shutdown – stop the loop
                break;
            }
        }
    }

    private async Task SendHeartbeatAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_settings.NodeId) ||
            string.IsNullOrWhiteSpace(_settings.BackendBaseUrl))
        {
            _logger.LogWarning(
                "NodeId or BackendBaseUrl is not configured. Skipping heartbeat.");
            return;
        }

        var payload = new
        {
            nodeId = _settings.NodeId,
            agentVersion = _settings.AgentVersion,
            timestamp = DateTimeOffset.UtcNow
        };

        try
        {
            var response = await _httpClient.PostAsJsonAsync(
                $"api/nodes/{Uri.EscapeDataString(_settings.NodeId)}/heartbeat",
                payload,
                cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation(
                    "Heartbeat sent for NodeId={NodeId} at {Time}",
                    _settings.NodeId,
                    DateTimeOffset.UtcNow);
            }
            else
            {
                _logger.LogWarning(
                    "Heartbeat returned {StatusCode} for NodeId={NodeId}",
                    (int)response.StatusCode,
                    _settings.NodeId);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogError(ex,
                "Failed to send heartbeat for NodeId={NodeId}",
                _settings.NodeId);
        }
    }
}
