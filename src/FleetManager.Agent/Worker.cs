using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using FleetManager.Agent.Options;
using FleetManager.Agent.Services;
using FleetManager.Contracts.Agent;
using Microsoft.Extensions.Options;

namespace FleetManager.Agent;

public sealed class Worker(
    ILogger<Worker> logger,
    IHttpClientFactory httpClientFactory,
    IOptions<AgentOptions> options) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        var client = httpClientFactory.CreateClient();
        client.BaseAddress = new Uri(settings.BackendBaseUrl);

        var nextHeartbeatAtUtc = DateTime.MinValue;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (DateTime.UtcNow >= nextHeartbeatAtUtc)
                {
                    await SendHeartbeatAsync(client, settings, stoppingToken);
                    nextHeartbeatAtUtc = DateTime.UtcNow.AddSeconds(settings.HeartbeatIntervalSeconds);
                }

                await PollAndExecuteCommandAsync(client, settings, stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Worker loop failed.");
            }

            await Task.Delay(TimeSpan.FromSeconds(settings.CommandPollIntervalSeconds), stoppingToken);
        }
    }

    private async Task SendHeartbeatAsync(HttpClient client, AgentOptions settings, CancellationToken cancellationToken)
    {
        var heartbeat = new AgentHeartbeatRequest
        {
            NodeId = settings.NodeId,
            AgentVersion = settings.AgentVersion,
            CpuPercent = 0,
            RamPercent = 0,
            DiskPercent = 0,
            RamUsedGb = 0,
            StorageUsedGb = 0,
            PingMs = 0,
            ActiveSessions = 0,
            ControlPort = settings.ControlPort,
            ConnectionState = settings.ConnectionState,
            ConnectionTimeoutSeconds = settings.ConnectionTimeoutSeconds
        };

        using var response = await client.PostAsJsonAsync("/api/agent/heartbeat", heartbeat, cancellationToken);
        logger.LogInformation("Heartbeat sent with status code: {StatusCode}", response.StatusCode);
    }

    private async Task PollAndExecuteCommandAsync(HttpClient client, AgentOptions settings, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync($"/api/agent/nodes/{settings.NodeId}/commands/next", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            return;
        }

        response.EnsureSuccessStatusCode();
        var command = await response.Content.ReadFromJsonAsync<AgentCommandEnvelopeResponse>(cancellationToken: cancellationToken);
        if (command is null)
        {
            return;
        }

        if (!CommandAllowlist.IsAllowed(command.CommandType))
        {
            await CompleteCommandAsync(client, settings.NodeId, command.CommandId, false, $"Command '{command.CommandType}' is not allowlisted.", cancellationToken);
            return;
        }

        var execution = await ExecuteCommandScriptAsync(command, settings, cancellationToken);
        await CompleteCommandAsync(client, settings.NodeId, command.CommandId, execution.Succeeded, execution.ResultMessage, cancellationToken);
    }

    private async Task<CommandExecutionResult> ExecuteCommandScriptAsync(AgentCommandEnvelopeResponse command, AgentOptions settings, CancellationToken cancellationToken)
    {
        var scriptsPath = Path.GetFullPath(settings.CommandScriptsPath);
        var scriptPath = Path.Combine(scriptsPath, $"{command.CommandType}.sh");
        if (!File.Exists(scriptPath))
        {
            return CommandExecutionResult.Failure($"Script not found: {scriptPath}");
        }

        var payloadDirectory = Path.Combine(Path.GetTempPath(), "fleetmanager-agent");
        Directory.CreateDirectory(payloadDirectory);
        var payloadPath = Path.Combine(payloadDirectory, $"{command.CommandId}.json");
        await File.WriteAllTextAsync(payloadPath, command.PayloadJson, Encoding.UTF8, cancellationToken);

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "/bin/bash",
                ArgumentList = { scriptPath, payloadPath },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = scriptsPath
            }
        };

        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        TryDeletePayloadFile(payloadPath, logger);

        var message = string.Join(Environment.NewLine, new[] { stdout.Trim(), stderr.Trim() }.Where(x => !string.IsNullOrWhiteSpace(x)));

        if (process.ExitCode == 0)
        {
            logger.LogInformation("Executed command {CommandType} successfully.", command.CommandType);
            return CommandExecutionResult.Success(string.IsNullOrWhiteSpace(message) ? "Command executed successfully." : message);
        }

        logger.LogWarning("Command {CommandType} failed with exit code {ExitCode}.", command.CommandType, process.ExitCode);
        return CommandExecutionResult.Failure(string.IsNullOrWhiteSpace(message) ? $"Command failed with exit code {process.ExitCode}." : message);
    }

    private async Task CompleteCommandAsync(HttpClient client, Guid nodeId, Guid commandId, bool succeeded, string resultMessage, CancellationToken cancellationToken)
    {
        var payload = new AgentCommandCompletionRequest
        {
            NodeId = nodeId,
            Succeeded = succeeded,
            ResultMessage = resultMessage
        };

        using var response = await client.PostAsJsonAsync($"/api/agent/commands/{commandId}/complete", payload, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private static void TryDeletePayloadFile(string path, ILogger logger)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to delete temporary payload file {PayloadPath}.", path);
        }
    }

    private sealed record CommandExecutionResult(bool Succeeded, string ResultMessage)
    {
        public static CommandExecutionResult Success(string message) => new(true, message);
        public static CommandExecutionResult Failure(string message) => new(false, message);
    }
}
