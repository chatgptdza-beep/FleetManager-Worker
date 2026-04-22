using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FleetManager.Contracts.Configuration;
using FleetManager.Contracts.Nodes;
using FleetManager.Desktop.Models;
using Renci.SshNet;

namespace FleetManager.Desktop.Services;

public class SshProvisioningService : ISshProvisioningService
{
    private const string ApiBundleRemotePath = "/tmp/fleetmanager-api-bundle.zip";
    private const string ApiConfigRemotePath = "/tmp/fleetmanager-api-appsettings.Production.json";
    private const string ApiServiceRemotePath = "/tmp/fleetmanager-api.service";
    private const string ApiBootstrapSqlRemotePath = "/tmp/fleetmanager-api-bootstrap.sql";
    private const string ApiTempExtractPath = "/tmp/fleetmanager-api-bundle";
    private const string ApiInstallDirectory = "/opt/fleetmanager/api";
    private const string ApiServicePath = "/etc/systemd/system/fleetmanager-api.service";
    private const string ApiServiceName = "fleetmanager-api";
    private const string ApiSystemUser = "fleetmgr";
    private const string ApiDatabaseName = "FleetManagerDb";
    private const string ApiDatabaseUser = "fleetmgr";
    private const string ApiProjectRelativePath = "src/FleetManager.Api/FleetManager.Api.csproj";
    private const string ApiAutoPublishRelativePath = "out/api";
    private const string AgentBundleRemotePath = "/tmp/fleetmanager-agent-bundle-linux-x64.zip";
    private const string AgentBundleSha256RemotePath = "/tmp/fleetmanager-agent-bundle-linux-x64.zip.sha256";
    private const string LinuxRuntimeIdentifier = "linux-x64";

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

    public async Task<DesktopNodeDiagnosticResult> DiagnoseNodeAsync(CreateNodeRequest request, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var connectionInfo = CreateConnectionInfo(request);
            using var client = new SshClient(connectionInfo);
            try
            {
                client.Connect();
                var commandText = BuildDiagnosticCommand();
                var command = client.CreateCommand(BuildElevatedCommand(request, commandText));
                command.CommandTimeout = TimeSpan.FromSeconds(20);
                var output = command.Execute();
                var lines = output
                    .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToDictionary(
                        line => line.Split('=', 2, StringSplitOptions.TrimEntries)[0],
                        line => line.Split('=', 2, StringSplitOptions.TrimEntries).Length > 1
                            ? line.Split('=', 2, StringSplitOptions.TrimEntries)[1]
                            : string.Empty,
                        StringComparer.OrdinalIgnoreCase);

                var agentActive = IsActive(lines, "FM_AGENT");
                var apiActive = IsActive(lines, "FM_API");
                var dockerWorkerActive = IsTruthy(lines, "FM_DOCKER_WORKER");

                var detailParts = new List<string>
                {
                    $"SSH ok to {request.SshUsername}@{request.IpAddress}:{request.SshPort}",
                    $"agent={(agentActive ? "active" : "missing")}",
                    $"api={(apiActive ? "active" : "missing")}",
                    $"docker-worker={(dockerWorkerActive ? "active" : "missing")}"
                };

                return new DesktopNodeDiagnosticResult
                {
                    IsSshReachable = true,
                    IsFleetManagerAgentActive = agentActive,
                    IsFleetManagerApiActive = apiActive,
                    IsDockerWorkerActive = dockerWorkerActive,
                    Detail = string.Join(" | ", detailParts)
                };
            }
            catch (Exception ex)
            {
                return new DesktopNodeDiagnosticResult
                {
                    IsSshReachable = false,
                    Detail = $"SSH failed for {request.IpAddress}: {ex.Message}"
                };
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

    public async Task InstallApiAsync(CreateNodeRequest request, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        progress?.Report("[API local] Resolving FleetManager.Api publish bundle...");
        var (localBundlePath, deleteAfterUse) = await ResolveApiBundlePathAsync(progress, cancellationToken);
        var databasePassword = ResolveDatabasePassword();
        var apiConfigJson = BuildApiAppSettingsJson(databasePassword);
        var apiServiceFile = BuildApiServiceFile();
        var bootstrapSql = BuildApiBootstrapSql(databasePassword);

        try
        {
            await Task.Run(() =>
            {
                var connectionInfo = CreateConnectionInfo(request);
                using var client = new SshClient(connectionInfo);
                using var sftp = new SftpClient(connectionInfo);

                client.Connect();
                sftp.Connect();

                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report("%12");
                progress?.Report("[API 1/6] Installing API prerequisites...");
                ExecuteStreaming(client, BuildElevatedCommand(request,
                    BuildAptUpdateAndInstallCommand("ca-certificates", "curl", "unzip", "postgresql", "postgresql-contrib")),
                    TimeSpan.FromMinutes(4), progress);

                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report("%16");
                progress?.Report("[API 2/6] Uploading FleetManager.Api bundle...");
                using (var bundleStream = File.OpenRead(localBundlePath))
                {
                    sftp.UploadFile(bundleStream, ApiBundleRemotePath, true);
                }

                UploadTextFile(sftp, ApiConfigRemotePath, apiConfigJson);
                UploadTextFile(sftp, ApiServiceRemotePath, apiServiceFile);
                UploadTextFile(sftp, ApiBootstrapSqlRemotePath, bootstrapSql);

                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report("%19");
                progress?.Report("[API 3/6] Preparing PostgreSQL and service account...");
                ExecuteStreaming(client, BuildElevatedCommand(request,
                    $"id -u {ApiSystemUser} >/dev/null 2>&1 || useradd --system --create-home --home-dir /home/{ApiSystemUser} --shell /usr/sbin/nologin {ApiSystemUser}\n" +
                    "systemctl enable --now postgresql\n" +
                    $"sudo -u postgres psql -d postgres -v ON_ERROR_STOP=1 -f {ApiBootstrapSqlRemotePath}\n" +
                    $"if ! sudo -u postgres psql -d postgres -tAc \"SELECT 1 FROM pg_database WHERE datname='{ApiDatabaseName.ToLowerInvariant()}'\" | grep -q 1; then\n" +
                    $"  sudo -u postgres psql -d postgres -c \"CREATE DATABASE {ApiDatabaseName.ToLowerInvariant()} OWNER {ApiDatabaseUser};\"\n" +
                    "fi\n" +
                    $"sudo -u postgres psql -d postgres -v ON_ERROR_STOP=1 -c \"ALTER DATABASE {ApiDatabaseName.ToLowerInvariant()} OWNER TO {ApiDatabaseUser}; GRANT ALL PRIVILEGES ON DATABASE {ApiDatabaseName.ToLowerInvariant()} TO {ApiDatabaseUser};\"\n" +
                    "if command -v ufw >/dev/null 2>&1; then ufw allow 5000/tcp || true; fi"),
                    TimeSpan.FromMinutes(4), progress);

                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report("%23");
                progress?.Report("[API 4/6] Deploying FleetManager.Api files...");
                ExecuteStreaming(client, BuildElevatedCommand(request,
                    $"systemctl stop {ApiServiceName} >/dev/null 2>&1 || true\n" +
                    $"rm -rf {ApiTempExtractPath} {ApiInstallDirectory}\n" +
                    $"mkdir -p {ApiTempExtractPath} {ApiInstallDirectory}\n" +
                    $"unzip -oq {ApiBundleRemotePath} -d {ApiTempExtractPath}\n" +
                    $"cp -R {ApiTempExtractPath}/. {ApiInstallDirectory}/\n" +
                    $"install -o {ApiSystemUser} -g {ApiSystemUser} -m 600 {ApiConfigRemotePath} {ApiInstallDirectory}/appsettings.Production.json\n" +
                    $"install -o root -g root -m 644 {ApiServiceRemotePath} {ApiServicePath}\n" +
                    $"chown -R {ApiSystemUser}:{ApiSystemUser} {ApiInstallDirectory}\n" +
                    $"chmod +x {ApiInstallDirectory}/FleetManager.Api"),
                    TimeSpan.FromMinutes(3), progress);

                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report("%27");
                progress?.Report("[API 5/6] Starting FleetManager.Api...");
                ExecuteStreaming(client, BuildElevatedCommand(request,
                    $"systemctl daemon-reload && systemctl enable {ApiServiceName} && systemctl restart {ApiServiceName}"),
                    TimeSpan.FromMinutes(2), progress);

                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report("%29");
                progress?.Report("[API 6/6] Waiting for remote API health...");
                ExecuteStreaming(client, BuildElevatedCommand(request,
                    $"for attempt in $(seq 1 45); do\n" +
                    "  if curl -fsS http://127.0.0.1:5000/health/ready >/dev/null; then exit 0; fi\n" +
                    "  sleep 2\n" +
                    "done\n" +
                    $"echo '{ApiServiceName} did not become healthy on localhost:5000.' >&2\n" +
                    $"systemctl status {ApiServiceName} --no-pager || true\n" +
                    $"journalctl -u {ApiServiceName} --no-pager -n 80 || true\n" +
                    "exit 1"),
                    TimeSpan.FromMinutes(3), progress);

                progress?.Report("%30");
                progress?.Report("Cleaning API temp files...");
                ExecuteStreaming(client, BuildElevatedCommand(request,
                    $"rm -rf {ApiTempExtractPath} {ApiBundleRemotePath} {ApiConfigRemotePath} {ApiServiceRemotePath} {ApiBootstrapSqlRemotePath}"),
                    TimeSpan.FromMinutes(1), progress);

                sftp.Disconnect();
                client.Disconnect();
                progress?.Report("FleetManager.Api deployed and healthy on the VPS.");
            }, cancellationToken);
        }
        finally
        {
            if (deleteAfterUse && File.Exists(localBundlePath))
            {
                File.Delete(localBundlePath);
            }
        }
    }

    public async Task InstallAgentAsync(CreateNodeRequest request, string apiBaseUrl, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        progress?.Report("[Agent source] Resolving FleetManager.Agent bundle source...");
        var localBundlePath = ResolveExplicitAgentBundlePath();
        var bundleUrl = string.IsNullOrWhiteSpace(localBundlePath) ? DesktopEnvironment.ResolveAgentBundleUrl() : string.Empty;
        var bundleSha256Url = string.IsNullOrWhiteSpace(localBundlePath) ? DesktopEnvironment.ResolveAgentBundleSha256Url() : string.Empty;
        var bundleSha256 = DesktopEnvironment.ResolveAgentBundleSha256();

        if (string.IsNullOrWhiteSpace(localBundlePath) && string.IsNullOrWhiteSpace(bundleUrl))
        {
            throw new InvalidOperationException(
                "Could not resolve a FleetManager.Agent release asset. Set FLEETMANAGER_AGENT_BUNDLE_URL or FLEETMANAGER_REPO_URL.");
        }

        await Task.Run(() =>
        {
            var connectionInfo = CreateConnectionInfo(request);
            using var client = new SshClient(connectionInfo);
            using var sftp = string.IsNullOrWhiteSpace(localBundlePath) ? null : new SftpClient(connectionInfo);

            client.Connect();
            sftp?.Connect();

            progress?.Report("%30");
            progress?.Report("[1/6] Installing download prerequisites...");
            ExecuteStreaming(client, BuildElevatedCommand(request,
                BuildAptUpdateAndInstallCommand("ca-certificates", "curl", "unzip")),
                TimeSpan.FromMinutes(2), progress);
            progress?.Report("%38");

            if (!string.IsNullOrWhiteSpace(localBundlePath))
            {
                progress?.Report("[2/6] Uploading FleetManager.Agent bundle from local override...");
                progress?.Report($"Bundle source: {localBundlePath}");
                using var bundleStream = File.OpenRead(localBundlePath);
                sftp!.UploadFile(bundleStream, AgentBundleRemotePath, true);
            }
            else
            {
                progress?.Report("[2/6] Downloading FleetManager.Agent bundle from GitHub Release...");
                progress?.Report($"Bundle source: {bundleUrl}");
                ExecuteStreaming(client, BuildElevatedCommand(request,
                    $"curl -fL {Quote(bundleUrl!)} -o {Quote(AgentBundleRemotePath)}"),
                    TimeSpan.FromMinutes(5), progress);
            }

            progress?.Report("%48");

            if (!string.IsNullOrWhiteSpace(bundleSha256))
            {
                progress?.Report("[3/6] Verifying bundle checksum...");
                ExecuteStreaming(client, BuildElevatedCommand(request,
                    $"echo {Quote($"{bundleSha256.Trim()}  {AgentBundleRemotePath}")} | sha256sum -c -"),
                    TimeSpan.FromMinutes(1), progress);
            }
            else if (string.IsNullOrWhiteSpace(localBundlePath) && !string.IsNullOrWhiteSpace(bundleSha256Url))
            {
                progress?.Report("[3/6] Downloading and verifying bundle checksum...");
                ExecuteStreaming(client, BuildElevatedCommand(request,
                    $"curl -fL {Quote(bundleSha256Url)} -o {Quote(AgentBundleSha256RemotePath)} && " +
                    $"(cd /tmp && sha256sum -c {Quote(FleetManagerReleaseDefaults.AgentBundleSha256FileName)})"),
                    TimeSpan.FromMinutes(2), progress);
            }
            else
            {
                progress?.Report("[3/6] No checksum configured. Skipping sha256 verification.");
            }

            progress?.Report("%60");
            progress?.Report("[4/6] Extracting bundle...");
            ExecuteStreaming(client, BuildElevatedCommand(request,
                $"rm -rf /tmp/fleetmanager-bundle && mkdir -p /tmp/fleetmanager-bundle && " +
                $"unzip -oq {Quote(AgentBundleRemotePath)} -d /tmp/fleetmanager-bundle"),
                TimeSpan.FromMinutes(3), progress);
            progress?.Report("%70");

            progress?.Report("[5/6] Installing agent and runtime dependencies...");
            ExecuteStreaming(client, BuildElevatedCommand(request,
                "bash /tmp/fleetmanager-bundle/deploy/linux/install-worker-ubuntu.sh /tmp/fleetmanager-bundle/agent"),
                TimeSpan.FromMinutes(10), progress);
            progress?.Report("%78");

            progress?.Report("[6/6] Cleaning up build artifacts...");
            ExecuteStreaming(client, BuildElevatedCommand(request,
                $"rm -rf /tmp/fleetmanager-bundle {Quote(AgentBundleRemotePath)} {Quote(AgentBundleSha256RemotePath)}"),
                TimeSpan.FromMinutes(1), progress);

            sftp?.Disconnect();
            client.Disconnect();
            progress?.Report("Agent installation finished successfully.");
        }, cancellationToken);
    }

    public Task<DesktopNodeDiagnosticResult> DiagnoseNodeAsync(DesktopManagedNodeRecord node, CancellationToken cancellationToken = default)
    {
        if (!node.HasStoredCredentials)
        {
            return Task.FromResult(new DesktopNodeDiagnosticResult
            {
                IsSshReachable = false,
                Detail = $"SSH credentials are not available for {node.CurrentIp}. Re-enter the SSH password or key in Node Registry."
            });
        }

        return DiagnoseNodeAsync(ToCreateNodeRequest(node), cancellationToken);
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
                    ApiKey = ResolveAgentApiKey(),
                    NodeIpAddress = request.IpAddress
                }
            }, new JsonSerializerOptions { WriteIndented = true });

            var configBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(configJson));

            var connectionInfo = CreateConnectionInfo(request);
            using var client = new SshClient(connectionInfo);
            client.Connect();

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

    private static async Task<(string bundlePath, bool deleteAfterUse)> ResolveApiBundlePathAsync(
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var explicitBundlePath = Environment.GetEnvironmentVariable("FLEETMANAGER_API_BUNDLE_PATH");
        if (!string.IsNullOrWhiteSpace(explicitBundlePath))
        {
            var normalizedPath = Path.GetFullPath(explicitBundlePath.Trim());
            if (!File.Exists(normalizedPath))
            {
                throw new FileNotFoundException(
                    "FLEETMANAGER_API_BUNDLE_PATH points to a file that does not exist.",
                    normalizedPath);
            }

            return (normalizedPath, false);
        }

        var publishDirectory = await ResolveApiPublishDirectoryAsync(progress, cancellationToken);
        var bundlePath = Path.Combine(Path.GetTempPath(), $"fleetmanager-api-bundle-{Guid.NewGuid():N}.zip");
        if (File.Exists(bundlePath))
        {
            File.Delete(bundlePath);
        }

        ZipFile.CreateFromDirectory(publishDirectory, bundlePath, CompressionLevel.SmallestSize, includeBaseDirectory: false);
        return (bundlePath, true);
    }

    private static string? ResolveExplicitAgentBundlePath()
    {
        var explicitBundlePath = DesktopEnvironment.ResolveAgentBundlePathOverride();
        if (!string.IsNullOrWhiteSpace(explicitBundlePath))
        {
            var normalizedPath = Path.GetFullPath(explicitBundlePath.Trim());
            if (!File.Exists(normalizedPath))
            {
                throw new FileNotFoundException(
                    "FLEETMANAGER_AGENT_BUNDLE_PATH points to a file that does not exist.",
                    normalizedPath);
            }

            EnsureLinuxCompatibleZipEntries(normalizedPath);
            return normalizedPath;
        }

        return null;
    }

    private static async Task<string> ResolveApiPublishDirectoryAsync(
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var explicitPublishDirectory = Environment.GetEnvironmentVariable("FLEETMANAGER_API_PUBLISH_DIR");
        if (!string.IsNullOrWhiteSpace(explicitPublishDirectory))
        {
            var normalizedPath = Path.GetFullPath(explicitPublishDirectory.Trim());
            if (File.Exists(Path.Combine(normalizedPath, "FleetManager.Api")))
            {
                return normalizedPath;
            }

            throw new DirectoryNotFoundException(
                "FLEETMANAGER_API_PUBLISH_DIR does not contain FleetManager.Api.");
        }

        var searchRoots = EnumerateSearchRoots().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var root in searchRoots)
        {
            foreach (var relativePath in new[]
                     {
                         Path.Combine("publish", "api"),
                         Path.Combine("out", "api"),
                         Path.Combine("src", "FleetManager.Api", "bin", "Release", "net8.0", LinuxRuntimeIdentifier, "publish")
                     })
            {
                var candidate = Path.Combine(root, relativePath);
                if (File.Exists(Path.Combine(candidate, "FleetManager.Api")))
                {
                    progress?.Report($"[API local] Found existing publish output at {candidate}");
                    return candidate;
                }
            }
        }

        var apiProjectPath = FindApiProjectPath(searchRoots);
        if (!string.IsNullOrWhiteSpace(apiProjectPath))
        {
            var workspaceRoot = Directory.GetParent(Directory.GetParent(Path.GetDirectoryName(apiProjectPath)!)!.FullName)!.FullName;
            var autoPublishDirectory = Path.Combine(workspaceRoot, ApiAutoPublishRelativePath);
            progress?.Report($"[API local] No publish output found. Running dotnet publish for {LinuxRuntimeIdentifier}...");
            await PublishApiAsync(apiProjectPath, autoPublishDirectory, progress, cancellationToken);

            if (File.Exists(Path.Combine(autoPublishDirectory, "FleetManager.Api")))
            {
                progress?.Report($"[API local] Created fresh publish output at {autoPublishDirectory}");
                return autoPublishDirectory;
            }

            throw new InvalidOperationException(
                $"dotnet publish completed but FleetManager.Api was not found in {autoPublishDirectory}.");
        }

        throw new InvalidOperationException(
            "Could not locate FleetManager.Api publish files. Set FLEETMANAGER_API_PUBLISH_DIR or FLEETMANAGER_API_BUNDLE_PATH.");
    }

    private static string? FindApiProjectPath(IEnumerable<string> searchRoots)
    {
        return FindExistingPath(searchRoots, ApiProjectRelativePath);
    }

    private static string? FindExistingPath(IEnumerable<string> searchRoots, string relativePath)
    {
        foreach (var root in searchRoots)
        {
            var candidate = Path.Combine(root, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static async Task PublishApiAsync(
        string apiProjectPath,
        string outputDirectory,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        if (Directory.Exists(outputDirectory))
        {
            Directory.Delete(outputDirectory, recursive: true);
        }

        Directory.CreateDirectory(outputDirectory);

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = Path.GetDirectoryName(apiProjectPath) ?? Directory.GetCurrentDirectory(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add("publish");
        startInfo.ArgumentList.Add(apiProjectPath);
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("Release");
        startInfo.ArgumentList.Add("-r");
        startInfo.ArgumentList.Add(LinuxRuntimeIdentifier);
        startInfo.ArgumentList.Add("--self-contained");
        startInfo.ArgumentList.Add("true");
        startInfo.ArgumentList.Add("-o");
        startInfo.ArgumentList.Add(outputDirectory);

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data))
            {
                progress?.Report($"  > {args.Data}");
            }
        };
        process.ErrorDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data))
            {
                progress?.Report($"  [err] {args.Data}");
            }
        };

        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to start dotnet publish for FleetManager.Api.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"dotnet publish for FleetManager.Api failed with exit code {process.ExitCode}.");
        }
    }

    private static void EnsureLinuxCompatibleZipEntries(string bundlePath)
    {
        using var archive = ZipFile.OpenRead(bundlePath);
        var invalidEntry = archive.Entries.FirstOrDefault(entry => entry.FullName.Contains('\\'));
        if (invalidEntry is not null)
        {
            throw new InvalidOperationException(
                $"The bundle '{bundlePath}' contains Windows path separators in entry '{invalidEntry.FullName}'.");
        }
    }

    private static IEnumerable<string> EnumerateSearchRoots()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            if (string.IsNullOrWhiteSpace(start) || !Directory.Exists(start))
            {
                continue;
            }

            var current = new DirectoryInfo(Path.GetFullPath(start));
            while (current is not null)
            {
                yield return current.FullName;
                current = current.Parent;
            }
        }
    }

    private static string BuildApiAppSettingsJson(string databasePassword)
    {
        var payload = new Dictionary<string, object?>
        {
            ["ConnectionStrings"] = new Dictionary<string, string>
            {
                ["DefaultConnection"] = $"Host=localhost;Database={ApiDatabaseName.ToLowerInvariant()};Username={ApiDatabaseUser};Password={databasePassword}"
            },
            ["Jwt"] = new Dictionary<string, string>
            {
                ["Key"] = ResolveJwtKey()
            },
            ["AdminPassword"] = ResolveOperatorPassword(),
            ["AgentApiKey"] = ResolveAgentApiKey()
        };

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
    }

    private static string BuildApiServiceFile()
        => $$"""
           [Unit]
           Description=FleetManager API
           Wants=network-online.target postgresql.service
           After=network-online.target postgresql.service

           [Service]
           Type=simple
           User={{ApiSystemUser}}
           Group={{ApiSystemUser}}
           WorkingDirectory={{ApiInstallDirectory}}
           Environment=ASPNETCORE_URLS=http://0.0.0.0:5000
           Environment=DOTNET_ENVIRONMENT=Production
           ExecStart={{ApiInstallDirectory}}/FleetManager.Api
           Restart=always
           RestartSec=5
           KillSignal=SIGINT

           [Install]
           WantedBy=multi-user.target
           """;

    private static string BuildApiBootstrapSql(string databasePassword)
        => $$"""
           DO $$
           BEGIN
               IF NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = '{{ApiDatabaseUser}}') THEN
                   CREATE ROLE {{ApiDatabaseUser}} LOGIN PASSWORD '{{databasePassword}}';
               ELSE
                   ALTER ROLE {{ApiDatabaseUser}} WITH LOGIN PASSWORD '{{databasePassword}}';
               END IF;
           END
           $$;
           """;

    private static void UploadTextFile(SftpClient client, string remotePath, string content)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(NormalizeForLinuxText(content)));
        client.UploadFile(stream, remotePath, true);
    }

    private static string ResolveOperatorPassword()
        => DesktopEnvironment.ResolveOperatorPassword();

    private static string ResolveAgentApiKey()
        => DesktopEnvironment.ResolveAgentApiKey();

    private static string ResolveJwtKey()
    {
        var configured = Environment.GetEnvironmentVariable("FLEETMANAGER_JWT_KEY");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured.Trim();
        }

        configured = Environment.GetEnvironmentVariable("Jwt__Key");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured.Trim();
        }

        return Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    }

    private static string ResolveDatabasePassword()
    {
        var configured = Environment.GetEnvironmentVariable("FLEETMANAGER_DB_PASSWORD");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured.Trim();
        }

        return Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
    }

    private static string BuildAptUpdateAndInstallCommand(params string[] packages)
    {
        var packageList = string.Join(" ", packages.Select(Quote));
        return $$"""
               wait_for_apt_locks() {
                 local waited=0
                 while fuser /var/lib/apt/lists/lock /var/lib/dpkg/lock-frontend /var/cache/apt/archives/lock >/dev/null 2>&1; do
                   echo "apt/dpkg is busy. Waiting for lock release..."
                   sleep 5
                   waited=$((waited+5))
                   if [ "$waited" -ge 300 ]; then
                     echo "Timed out waiting for apt/dpkg lock release." >&2
                     return 1
                   fi
                 done
               }

               run_apt() {
                 local retries=0
                 while true; do
                   wait_for_apt_locks || return 1
                   if "$@"; then
                     return 0
                   fi

                   if fuser /var/lib/apt/lists/lock /var/lib/dpkg/lock-frontend /var/cache/apt/archives/lock >/dev/null 2>&1; then
                     retries=$((retries+1))
                     echo "apt/dpkg became busy again. Retrying..."
                     sleep 5
                     if [ "$retries" -ge 60 ]; then
                       echo "Timed out retrying apt command while lock is held." >&2
                       return 1
                     fi
                     continue
                   fi

                   return 1
                 done
               }

               run_apt apt-get update
               run_apt env DEBIAN_FRONTEND=noninteractive apt-get install -y {{packageList}}
               """;
    }

    private static void ExecuteStreaming(SshClient client, string commandText, TimeSpan timeout, IProgress<string>? progress)
    {
        using var command = client.CreateCommand(commandText);
        command.CommandTimeout = timeout;
        var asyncResult = command.BeginExecute();

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
        var safeCommand = NormalizeForLinuxText("set -euo pipefail\n" + command);

        if (string.Equals(request.SshUsername, "root", StringComparison.OrdinalIgnoreCase))
        {
            return $"bash -lc {Quote(safeCommand)}";
        }

        if (!string.IsNullOrWhiteSpace(request.SshPassword))
        {
            return $"printf '%s\\n' {Quote(request.SshPassword)} | sudo -S -- bash -lc {Quote(safeCommand)}";
        }

        return $"sudo -- bash -lc {Quote(safeCommand)}";
    }

    private static string BuildElevatedCommand(DesktopManagedNodeRecord node, string command)
        => BuildElevatedCommand(ToCreateNodeRequest(node), command);

    private static string Quote(string value)
        => $"'{value.Replace("'", "'\"'\"'")}'";

    private static string NormalizeForLinuxText(string value)
        => value.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);

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

    private static ConnectionInfo CreateConnectionInfo(DesktopManagedNodeRecord node)
        => CreateConnectionInfo(ToCreateNodeRequest(node));

    private static CreateNodeRequest ToCreateNodeRequest(DesktopManagedNodeRecord node)
        => new()
        {
            Name = node.Name,
            IpAddress = node.CurrentIp,
            SshPort = node.SshPort,
            ControlPort = node.ControlPort,
            SshUsername = node.SshUsername,
            SshPassword = DesktopCredentialProtector.Unprotect(node.EncryptedSshPassword),
            SshPrivateKey = DesktopCredentialProtector.Unprotect(node.EncryptedSshPrivateKey),
            AuthType = node.AuthType,
            OsType = node.OsType,
            Region = node.Region
        };

    private static string BuildDiagnosticCommand()
        => """
           printf 'FM_AGENT='
           systemctl is-active fleetmanager-agent 2>/dev/null || true
           printf 'FM_API='
           systemctl is-active fleetmanager-api 2>/dev/null || true
           if command -v docker >/dev/null 2>&1 && docker ps --format '{{.Names}}' 2>/dev/null | grep -qi 'worker_app'; then
             echo 'FM_DOCKER_WORKER=1'
           else
             echo 'FM_DOCKER_WORKER=0'
           fi
           """;

    private static bool IsActive(IReadOnlyDictionary<string, string> values, string key)
        => values.TryGetValue(key, out var value)
            && string.Equals(value.Trim(), "active", StringComparison.OrdinalIgnoreCase);

    private static bool IsTruthy(IReadOnlyDictionary<string, string> values, string key)
        => values.TryGetValue(key, out var value)
            && (string.Equals(value.Trim(), "1", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value.Trim(), "true", StringComparison.OrdinalIgnoreCase));
}
