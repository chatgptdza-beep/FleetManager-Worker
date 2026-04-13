using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FleetManager.Contracts.Nodes;
using Renci.SshNet;

namespace FleetManager.Desktop.Services;

public class SshProvisioningService : ISshProvisioningService
{
    private const string DefaultRepoUrl = "https://github.com/chatgptdza-beep/FleetManager-Worker.git";
    private const string DefaultBranch = "main";

    public async Task<bool> TestConnectionAsync(CreateNodeRequest request, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var connectionInfo = CreateConnectionInfo(request);
            using var client = new SshClient(connectionInfo);
            try
            {
                client.Connect();
                return client.IsConnected;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (client.IsConnected)
                {
                    client.Disconnect();
                }
            }
        }, cancellationToken);
    }

    public async Task<bool> IsAgentRunningAsync(CreateNodeRequest request, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var connectionInfo = CreateConnectionInfo(request);
            using var client = new SshClient(connectionInfo);
            try
            {
                client.Connect();
                var cmd = client.CreateCommand(BuildElevatedCommand(request, "systemctl is-active fleetmanager-agent"));
                var result = cmd.Execute();
                return cmd.ExitStatus == 0 && string.Equals(result.Trim(), "active", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
            finally
            {
                if (client.IsConnected)
                {
                    client.Disconnect();
                }
            }
        }, cancellationToken);
    }

    public async Task InstallAgentAsync(CreateNodeRequest request, string apiBaseUrl, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var repoUrl = Environment.GetEnvironmentVariable("FLEETMANAGER_REPO_URL") ?? DefaultRepoUrl;
        var branch = Environment.GetEnvironmentVariable("FLEETMANAGER_REPO_BRANCH") ?? DefaultBranch;

        await Task.Run(() =>
        {
            var connectionInfo = CreateConnectionInfo(request);
            using var client = new SshClient(connectionInfo);
            client.Connect();

            progress?.Report("%30");
            progress?.Report("[1/6] Installing system dependencies...");
            ExecuteStreaming(client, BuildElevatedCommand(request,
                "apt-get update && DEBIAN_FRONTEND=noninteractive apt-get install -y " +
                "ca-certificates curl git python3 procps xz-utils xvfb x11vnc fluxbox novnc websockify"),
                TimeSpan.FromMinutes(5), progress);

            ExecuteStreaming(client, BuildElevatedCommand(request,
                "command -v chromium >/dev/null 2>&1 || command -v chromium-browser >/dev/null 2>&1 || " +
                "DEBIAN_FRONTEND=noninteractive apt-get install -y chromium-browser || " +
                "DEBIAN_FRONTEND=noninteractive apt-get install -y chromium || true"),
                TimeSpan.FromMinutes(3), progress);
            progress?.Report("%38");

            progress?.Report("[2/6] Installing .NET 8 runtime...");
            ExecuteStreaming(client, BuildElevatedCommand(request,
                "command -v dotnet >/dev/null 2>&1 || { " +
                "curl -sSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh && " +
                "bash /tmp/dotnet-install.sh --channel 8.0 --install-dir /usr/share/dotnet && " +
                "ln -sf /usr/share/dotnet/dotnet /usr/bin/dotnet && " +
                "rm -f /tmp/dotnet-install.sh; }"),
                TimeSpan.FromMinutes(5), progress);
            progress?.Report("%48");

            progress?.Report("[3/6] Cloning repository...");
            ExecuteStreaming(client, BuildElevatedCommand(request,
                $"rm -rf /tmp/fleetmanager-repo && git clone --depth 1 --branch {Quote(branch)} {Quote(repoUrl)} /tmp/fleetmanager-repo"),
                TimeSpan.FromMinutes(3), progress);
            progress?.Report("%55");

            progress?.Report("[4/6] Building agent (dotnet publish — this may take a few minutes)...");
            ExecuteStreaming(client, BuildElevatedCommand(request,
                "cd /tmp/fleetmanager-repo/src/FleetManager.Agent && " +
                "dotnet publish -c Release -r linux-x64 --self-contained false -o /tmp/fleetmanager-build"),
                TimeSpan.FromMinutes(8), progress);
            progress?.Report("%68");

            progress?.Report("[5/6] Running install script...");
            ExecuteStreaming(client, BuildElevatedCommand(request,
                "bash /tmp/fleetmanager-repo/deploy/linux/install-worker-ubuntu.sh /tmp/fleetmanager-build"),
                TimeSpan.FromMinutes(3), progress);
            progress?.Report("%75");

            progress?.Report("[6/6] Cleaning up build artifacts...");
            ExecuteStreaming(client, BuildElevatedCommand(request,
                "rm -rf /tmp/fleetmanager-repo /tmp/fleetmanager-build"),
                TimeSpan.FromMinutes(1), progress);

            client.Disconnect();
            progress?.Report("Agent installation finished successfully.");
        }, cancellationToken);
    }

    public async Task ConfigureAgentAsync(CreateNodeRequest request, Guid nodeId, string apiBaseUrl, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        await Task.Run(() =>
        {
            progress?.Report("%82");
            progress?.Report("Configuring agent appsettings...");

            var configJson = JsonSerializer.Serialize(new
            {
                Agent = new
                {
                    NodeId = nodeId,
                    BackendBaseUrl = apiBaseUrl,
                    HeartbeatIntervalSeconds = 15,
                    CommandPollIntervalSeconds = 3,
                    AgentVersion = "1.0.0",
                    ControlPort = request.ControlPort,
                    ConnectionState = "Connected",
                    ConnectionTimeoutSeconds = 5,
                    CommandScriptsPath = "/opt/fleetmanager-agent/commands",
                    ApiKey = ResolveAgentApiKey(apiBaseUrl),
                    NodeIpAddress = request.IpAddress
                }
            }, new JsonSerializerOptions { WriteIndented = true });

            // Encode config as base64 to avoid shell escaping issues (no SFTP needed)
            var configBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(configJson));

            var connectionInfo = CreateConnectionInfo(request);
            using var client = new SshClient(connectionInfo);
            client.Connect();

            // Write config via base64 decode — safe for any JSON content
            ExecuteStreaming(client, BuildElevatedCommand(request,
                $"echo {Quote(configBase64)} | base64 -d > /opt/fleetmanager-agent/appsettings.json"),
                TimeSpan.FromMinutes(1), progress);

            progress?.Report("%88");
            progress?.Report("Setting permissions and restarting agent service...");
            ExecuteStreaming(client, BuildElevatedCommand(request,
                "chown fleetmgr:fleetmgr /opt/fleetmanager-agent/appsettings.json && " +
                "chmod 600 /opt/fleetmanager-agent/appsettings.json && " +
                "systemctl restart fleetmanager-agent"),
                TimeSpan.FromMinutes(2), progress);

            client.Disconnect();
            progress?.Report("%93");
            progress?.Report("Agent configured and restarted successfully.");
        }, cancellationToken);
    }

    private static string ResolveAgentApiKey(string apiBaseUrl)
    {
        var configured = Environment.GetEnvironmentVariable("FLEETMANAGER_AGENT_API_KEY");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured.Trim();
        }

        return "MASTER-KEY-12345";
    }

    /// <summary>
    /// Execute a remote SSH command and stream stdout lines to the progress reporter in real time.
    /// Uses BeginExecute + OutputStream so output appears as it is produced.
    /// </summary>
    private static void ExecuteStreaming(SshClient client, string commandText, TimeSpan timeout, IProgress<string>? progress)
    {
        using var command = client.CreateCommand(commandText);
        command.CommandTimeout = timeout;
        var asyncResult = command.BeginExecute();

        // Read stdout lines in real-time — OutputStream blocks until data arrives or EOF
        using var reader = new StreamReader(command.OutputStream, Encoding.UTF8);
        while (!reader.EndOfStream)
        {
            var line = reader.ReadLine();
            if (line != null)
            {
                progress?.Report($"  > {line}");
            }
        }

        command.EndExecute(asyncResult);

        // Report stderr after completion
        if (!string.IsNullOrWhiteSpace(command.Error))
        {
            foreach (var errLine in command.Error.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                progress?.Report($"  [err] {errLine.TrimEnd()}");
            }
        }

        if (command.ExitStatus != 0)
        {
            throw new InvalidOperationException(
                $"Remote command failed with exit code {command.ExitStatus}: {command.Error}");
        }
    }

    private static string BuildElevatedCommand(CreateNodeRequest request, string command)
    {
        if (string.Equals(request.SshUsername, "root", StringComparison.OrdinalIgnoreCase))
        {
            return command;
        }

        if (!string.IsNullOrWhiteSpace(request.SshPassword))
        {
            return $"printf '%s\\n' {Quote(request.SshPassword)} | sudo -S -- sh -lc {Quote(command)}";
        }

        return $"sudo -- sh -lc {Quote(command)}";
    }

    private static string Quote(string value)
        => $"'{value.Replace("'", "'\"'\"'")}'";

    private static ConnectionInfo CreateConnectionInfo(CreateNodeRequest request)
    {
        AuthenticationMethod authMethod;

        if (string.Equals(request.AuthType, "SshKey", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(request.SshPrivateKey))
        {
            var memoryStream = new MemoryStream(Encoding.UTF8.GetBytes(request.SshPrivateKey));
            var privateKeyFile = new PrivateKeyFile(memoryStream);
            authMethod = new PrivateKeyAuthenticationMethod(request.SshUsername, privateKeyFile);
        }
        else
        {
            authMethod = new PasswordAuthenticationMethod(request.SshUsername, request.SshPassword ?? string.Empty);
        }

        return new ConnectionInfo(request.IpAddress, request.SshPort, request.SshUsername, authMethod);
    }
}
