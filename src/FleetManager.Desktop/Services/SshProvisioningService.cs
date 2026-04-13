using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FleetManager.Contracts.Nodes;
using Renci.SshNet;
using Renci.SshNet.Sftp;

namespace FleetManager.Desktop.Services;

public class SshProvisioningService : ISshProvisioningService
{
    private const string RemotePackageDir = "/tmp/fleetmanager-agent";
    private const string RemoteDeployDir = "/tmp/fleetmanager-deploy";
    private const string RemoteConfigPath = "/tmp/fleetmanager-agent.appsettings.json";
    private const string ChecksumManifestName = ".fleetmanager.sha256";

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
        await Task.Run(() =>
        {
            progress?.Report("%30");
            progress?.Report("[1/6] Resolving local agent package...");
            var packageDirectory = ResolveAgentPackageDirectory();
            var deployDirectory = ResolveLinuxDeployDirectory();
            var connectionInfo = CreateConnectionInfo(request);

            progress?.Report("%35");
            progress?.Report("[2/6] Connecting via SSH + SFTP...");
            using var sshClient = new SshClient(connectionInfo);
            using var sftpClient = new SftpClient(connectionInfo);

            sshClient.Connect();
            sftpClient.Connect();

            EnsureRemoteDirectory(sftpClient, RemotePackageDir);
            EnsureRemoteDirectory(sftpClient, RemoteDeployDir);

            progress?.Report("%40");
            progress?.Report("[3/6] Cleaning remote directories...");
            ExecuteChecked(sshClient, $"rm -rf {Quote(RemotePackageDir)}/* {Quote(RemoteDeployDir)}/*", TimeSpan.FromMinutes(1), progress);

            progress?.Report("%45");
            progress?.Report("[4/6] Uploading agent package via SFTP...");
            UploadDirectory(sftpClient, packageDirectory, RemotePackageDir);
            UploadDirectory(sftpClient, deployDirectory, RemoteDeployDir);
            UploadTextFile(sftpClient, $"{RemotePackageDir}/{ChecksumManifestName}", BuildChecksumManifest(packageDirectory));
            progress?.Report("%55");
            progress?.Report("[4/6] Upload complete.");

            progress?.Report("[5/6] Running install-worker-ubuntu.sh (this may take a few minutes)...");
            var installCommand = BuildElevatedCommand(
                request,
                $"bash {Quote($"{RemoteDeployDir}/install-worker-ubuntu.sh")} {Quote(RemotePackageDir)}");
            ExecuteChecked(sshClient, installCommand, TimeSpan.FromMinutes(10), progress);
            progress?.Report("%75");
            progress?.Report("[5/6] Install script completed.");

            progress?.Report("[6/6] Disconnecting...");
            sftpClient.Disconnect();
            sshClient.Disconnect();
            progress?.Report("Agent installation finished successfully.");
        }, cancellationToken);
    }

    public async Task ConfigureAgentAsync(CreateNodeRequest request, Guid nodeId, string apiBaseUrl, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        await Task.Run(() =>
        {
            progress?.Report("%82");
            progress?.Report("Configuring agent appsettings...");
            var connectionInfo = CreateConnectionInfo(request);
            using var sshClient = new SshClient(connectionInfo);
            using var sftpClient = new SftpClient(connectionInfo);

            sshClient.Connect();
            sftpClient.Connect();

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

            UploadTextFile(sftpClient, RemoteConfigPath, configJson);
            progress?.Report("%88");
            progress?.Report("Deploying appsettings and restarting agent service...");
            var configureCommand = BuildElevatedCommand(
                request,
                $"install -m 600 {Quote(RemoteConfigPath)} /opt/fleetmanager-agent/appsettings.json && chown fleetmgr:fleetmgr /opt/fleetmanager-agent/appsettings.json && systemctl restart fleetmanager-agent");
            ExecuteChecked(sshClient, configureCommand, TimeSpan.FromMinutes(2), progress);

            sftpClient.Disconnect();
            sshClient.Disconnect();
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

        if (Uri.TryCreate(apiBaseUrl, UriKind.Absolute, out var apiUri)
            && (string.Equals(apiUri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
                || string.Equals(apiUri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase)))
        {
            return "MASTER-KEY-12345";
        }

        throw new InvalidOperationException(
            "Missing FLEETMANAGER_AGENT_API_KEY for remote worker configuration.");
    }

    private static string BuildChecksumManifest(string localDirectory)
    {
        var files = Directory
            .EnumerateFiles(localDirectory, "*", SearchOption.AllDirectories)
            .Where(file => !string.Equals(Path.GetFileName(file), ChecksumManifestName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (files.Length == 0)
        {
            throw new InvalidOperationException("Agent package directory is empty.");
        }

        var builder = new StringBuilder();
        foreach (var file in files)
        {
            using var stream = File.OpenRead(file);
            var hash = SHA256.HashData(stream);
            var relativePath = Path.GetRelativePath(localDirectory, file).Replace('\\', '/');
            builder.Append(Convert.ToHexString(hash).ToLowerInvariant())
                .Append("  ")
                .Append(relativePath)
                .AppendLine();
        }

        return builder.ToString();
    }

    private static string ResolveAgentPackageDirectory()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.CurrentDirectory, "out", "agent"),
            Path.Combine(AppContext.BaseDirectory, "out", "agent"),
            Path.Combine(Environment.CurrentDirectory, "md file", "01_full_project_latest", "out", "agent"),
            Path.Combine(AppContext.BaseDirectory, "md file", "01_full_project_latest", "out", "agent"),
            Path.Combine(Environment.CurrentDirectory, "src", "FleetManager.Agent", "bin", "Release", "net8.0", "linux-x64", "publish"),
            Path.Combine(AppContext.BaseDirectory, "src", "FleetManager.Agent", "bin", "Release", "net8.0", "linux-x64", "publish"),
            Path.Combine(Environment.CurrentDirectory, "src", "FleetManager.Agent", "bin", "Debug", "net8.0", "linux-x64", "publish"),
            Path.Combine(AppContext.BaseDirectory, "src", "FleetManager.Agent", "bin", "Debug", "net8.0", "linux-x64", "publish"),
            Path.Combine(Environment.CurrentDirectory, "src", "FleetManager.Agent", "bin", "Debug", "net8.0"),
            Path.Combine(AppContext.BaseDirectory, "src", "FleetManager.Agent", "bin", "Debug", "net8.0")
        };

        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (Directory.Exists(candidate)
                && (File.Exists(Path.Combine(candidate, "FleetManager.Agent"))
                    || File.Exists(Path.Combine(candidate, "FleetManager.Agent.exe"))
                    || File.Exists(Path.Combine(candidate, "FleetManager.Agent.dll"))))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException(
            "Unable to locate a local FleetManager.Agent package to upload. Run scripts/publish-agent.ps1 first.");
    }

    private static string ResolveLinuxDeployDirectory()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.CurrentDirectory, "deploy", "linux"),
            Path.Combine(AppContext.BaseDirectory, "deploy", "linux"),
            Path.Combine(Environment.CurrentDirectory, "md file", "01_full_project_latest", "deploy", "linux"),
            Path.Combine(AppContext.BaseDirectory, "md file", "01_full_project_latest", "deploy", "linux")
        };

        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "install-worker-ubuntu.sh")))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("Unable to locate local deploy/linux assets required for worker installation.");
    }

    private static void UploadDirectory(SftpClient sftpClient, string localDirectory, string remoteDirectory)
    {
        EnsureRemoteDirectory(sftpClient, remoteDirectory);

        foreach (var directory in Directory.GetDirectories(localDirectory))
        {
            var remoteChild = $"{remoteDirectory}/{Path.GetFileName(directory)}";
            UploadDirectory(sftpClient, directory, remoteChild);
        }

        foreach (var file in Directory.GetFiles(localDirectory))
        {
            var remotePath = $"{remoteDirectory}/{Path.GetFileName(file)}";
            var extension = Path.GetExtension(file);

            // Ensure shell scripts use Unix line endings (LF) even when built on Windows
            if (string.Equals(extension, ".sh", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".service", StringComparison.OrdinalIgnoreCase))
            {
                var text = File.ReadAllText(file).Replace("\r\n", "\n").Replace("\r", "\n");
                using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
                sftpClient.UploadFile(stream, remotePath, true);
            }
            else
            {
                using var fileStream = File.OpenRead(file);
                sftpClient.UploadFile(fileStream, remotePath, true);
            }
        }
    }

    private static void UploadTextFile(SftpClient sftpClient, string remotePath, string content)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        sftpClient.UploadFile(stream, remotePath, true);
    }

    private static void EnsureRemoteDirectory(SftpClient sftpClient, string remoteDirectory)
    {
        var parts = remoteDirectory.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var current = remoteDirectory.StartsWith("/", StringComparison.Ordinal) ? "/" : string.Empty;

        foreach (var part in parts)
        {
            current = string.IsNullOrEmpty(current) || current == "/" ? $"{current}{part}" : $"{current}/{part}";
            if (!sftpClient.Exists(current))
            {
                sftpClient.CreateDirectory(current);
            }
        }
    }

    private static void ExecuteChecked(SshClient client, string commandText, TimeSpan timeout, IProgress<string>? progress = null)
    {
        var command = client.CreateCommand(commandText);
        command.CommandTimeout = timeout;
        var output = command.Execute();

        if (!string.IsNullOrWhiteSpace(output))
        {
            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                progress?.Report($"  > {line.TrimEnd()}");
            }
        }

        if (!string.IsNullOrWhiteSpace(command.Error))
        {
            foreach (var line in command.Error.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                progress?.Report($"  [err] {line.TrimEnd()}");
            }
        }

        if (command.ExitStatus != 0)
        {
            throw new InvalidOperationException(
                $"Remote command failed with exit code {command.ExitStatus}: {command.Error}{Environment.NewLine}{output}");
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
