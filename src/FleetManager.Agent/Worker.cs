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
    IOptions<AgentOptions> options,
    LinuxMetricsCollector metrics) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        var client = httpClientFactory.CreateClient("AgentClient");
        client.BaseAddress = new Uri(settings.BackendBaseUrl);
        if (!string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            client.DefaultRequestHeaders.Remove("X-Api-Key");
            client.DefaultRequestHeaders.Add("X-Api-Key", settings.ApiKey.Trim());
        }

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
        var cpu                   = metrics.GetCpuPercent();
        var (ramPercent, ramUsed) = metrics.GetRamInfo();
        var (diskPercent, diskUsed) = metrics.GetDiskInfo();
        var activeSessions        = LinuxMetricsCollector.GetActiveSessionCount();
        var pingMs                = await MeasurePingAsync(client, cancellationToken);

        var heartbeat = new AgentHeartbeatRequest
        {
            NodeId                  = settings.NodeId,
            AgentVersion            = settings.AgentVersion,
            CpuPercent              = cpu,
            RamPercent              = ramPercent,
            DiskPercent             = diskPercent,
            RamUsedGb               = ramUsed,
            StorageUsedGb           = diskUsed,
            PingMs                  = pingMs,
            ActiveSessions          = activeSessions,
            ControlPort             = settings.ControlPort,
            ConnectionState         = settings.ConnectionState,
            ConnectionTimeoutSeconds = settings.ConnectionTimeoutSeconds
        };

        using var response = await client.PostAsJsonAsync("/api/agent/heartbeat", heartbeat, cancellationToken);
        logger.LogInformation(
            "Heartbeat sent — CPU:{Cpu}% RAM:{Ram}% Disk:{Disk}% Sessions:{Sessions} Ping:{Ping}ms — HTTP {StatusCode}",
            cpu, ramPercent, diskPercent, activeSessions, pingMs, response.StatusCode);
    }

    private static async Task<int> MeasurePingAsync(HttpClient client, CancellationToken cancellationToken)
    {
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            using var resp = await client.GetAsync("/health", cancellationToken);
            sw.Stop();
            return (int)sw.ElapsedMilliseconds;
        }
        catch
        {
            return -1;
        }
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

        CommandExecutionResult execution;
        try
        {
            execution = await ExecuteCommandScriptAsync(command, settings, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception while executing command {CommandType} ({CommandId}).", command.CommandType, command.CommandId);
            execution = CommandExecutionResult.Failure($"Unhandled agent error: {ex.Message}");
        }

        await CompleteCommandAsync(client, settings.NodeId, command.CommandId, execution.Succeeded, execution.ResultMessage, cancellationToken);
    }

    private async Task<CommandExecutionResult> ExecuteCommandScriptAsync(AgentCommandEnvelopeResponse command, AgentOptions settings, CancellationToken cancellationToken)
    {
        var scriptsPath = Path.GetFullPath(settings.CommandScriptsPath);

        // Guard against path traversal: command type must not contain path separators
        if (command.CommandType.Contains('/') || command.CommandType.Contains('\\') || command.CommandType.Contains(".."))
        {
            logger.LogWarning("Rejected command with suspicious CommandType: {CommandType}", command.CommandType);
            return CommandExecutionResult.Failure($"Invalid command type: '{command.CommandType}' contains illegal path characters.");
        }

        var scriptPath = Path.GetFullPath(Path.Combine(scriptsPath, $"{command.CommandType}.sh"));

        // Ensure the resolved path is still inside the scripts directory
        if (!scriptPath.StartsWith(scriptsPath + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !scriptPath.Equals(scriptsPath, StringComparison.Ordinal))
        {
            logger.LogWarning("Path traversal attempt detected. CommandType: {CommandType} resolved to {ScriptPath}",
                command.CommandType, scriptPath);
            return CommandExecutionResult.Failure($"Command '{command.CommandType}' resolved outside the scripts directory.");
        }

        if (!File.Exists(scriptPath))
        {
            return CommandExecutionResult.Failure($"Script not found: {scriptPath}");
        }

        var payloadDirectory = ResolveWritablePayloadDirectory();
        Directory.CreateDirectory(payloadDirectory);
        var payloadPath = Path.Combine(payloadDirectory, $"{command.CommandId}.json");
        await File.WriteAllTextAsync(payloadPath, command.PayloadJson, new UTF8Encoding(false), cancellationToken);

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

        var browserExtensions = BuildBrowserExtensionsEnvironmentValue(settings.BrowserExtensions);
        if (!string.IsNullOrWhiteSpace(browserExtensions))
        {
            process.StartInfo.Environment["FM_BROWSER_EXTENSIONS"] = browserExtensions;
        }

        // Enforce a maximum execution time to prevent runaway scripts
        var commandTimeout = TimeSpan.FromMinutes(settings.CommandTimeoutMinutes);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(commandTimeout);

        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Timeout reached — kill the process
            try { process.Kill(entireProcessTree: true); } catch { /* best-effort */ }
            TryDeletePayloadFile(payloadPath, logger);
            logger.LogWarning("Command {CommandType} timed out after {Timeout} minutes.", command.CommandType, settings.CommandTimeoutMinutes);
            return CommandExecutionResult.Failure($"Command '{command.CommandType}' timed out after {settings.CommandTimeoutMinutes} minutes.");
        }

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

    private static string ResolveWritablePayloadDirectory()
    {
        var candidates = new[]
        {
            Path.Combine(Path.GetTempPath(), "fleetmanager-agent"),
            "/var/lib/fleetmanager/tmp",
            Path.Combine(AppContext.BaseDirectory, "tmp")
        };

        foreach (var candidate in candidates)
        {
            if (TryEnsureWritableDirectory(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("No writable payload directory is available for command execution.");
    }

    private static bool TryEnsureWritableDirectory(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            var probePath = Path.Combine(path, $".probe-{Guid.NewGuid():N}");
            File.WriteAllText(probePath, "ok", new UTF8Encoding(false));
            File.Delete(probePath);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string BuildBrowserExtensionsEnvironmentValue(IEnumerable<string>? extensionPaths)
    {
        if (extensionPaths is null)
        {
            return string.Empty;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rawPath in extensionPaths)
        {
            if (string.IsNullOrWhiteSpace(rawPath))
            {
                continue;
            }

            var trimmed = rawPath.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            seen.Add(trimmed);
        }

        return seen.Count == 0 ? string.Empty : string.Join(',', seen);
    }

    private sealed record CommandExecutionResult(bool Succeeded, string ResultMessage)
    {
        public static CommandExecutionResult Success(string message) => new(true, message);
        public static CommandExecutionResult Failure(string message) => new(false, message);
    }
}
