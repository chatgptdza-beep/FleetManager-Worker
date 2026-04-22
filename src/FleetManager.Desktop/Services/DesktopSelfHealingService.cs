using System.Net.Http;
using FleetManager.Desktop.Models;

namespace FleetManager.Desktop.Services;

public sealed class DesktopSelfHealingService(
    IDesktopNodeRegistry nodeRegistry,
    ISshProvisioningService sshProvisioningService,
    IDashboardDataService dashboardDataService,
    ISshTunnelManager tunnelManager) : IDesktopSelfHealingService
{
    private static readonly TimeSpan HealingInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaxWorkerHeartbeatAge = TimeSpan.FromMinutes(2);

    private readonly SemaphoreSlim _cycleLock = new(1, 1);
    private readonly HttpClient _probeClient = new() { Timeout = TimeSpan.FromSeconds(4) };
    private CancellationTokenSource? _lifetimeCts;
    private Task? _loopTask;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_loopTask is not null)
        {
            return Task.CompletedTask;
        }

        _lifetimeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loopTask = RunAsync(_lifetimeCts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_lifetimeCts is null || _loopTask is null)
        {
            return;
        }

        _lifetimeCts.Cancel();
        try
        {
            await _loopTask.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _lifetimeCts.Dispose();
            _lifetimeCts = null;
            _loopTask = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        await tunnelManager.CloseAllAsync();
        _cycleLock.Dispose();
        _probeClient.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(HealingInterval);
        await RunCycleAsync(cancellationToken);

        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            await RunCycleAsync(cancellationToken);
        }
    }

    private async Task RunCycleAsync(CancellationToken cancellationToken)
    {
        if (!await _cycleLock.WaitAsync(0, cancellationToken))
        {
            return;
        }

        try
        {
            var nodes = await nodeRegistry.GetNodesAsync(cancellationToken);
            foreach (var node in nodes)
            {
                await HealNodeAsync(node, cancellationToken);
            }
        }
        finally
        {
            _cycleLock.Release();
        }
    }

    private async Task HealNodeAsync(DesktopManagedNodeRecord node, CancellationToken cancellationToken)
    {
        var checkedAtUtc = DateTime.UtcNow;
        var recoveryReason = "SSH is available. Reinstalling and reconfiguring the worker automatically.";

        if (string.IsNullOrWhiteSpace(node.CurrentIp))
        {
            await nodeRegistry.UpdateNodeHealthAsync(
                node.WorkflowNodeId,
                DesktopManagedNodeStatus.ActionRequired,
                "Current IP is missing. Update the node endpoint in the desktop registry.",
                checkedAtUtc,
                cancellationToken: cancellationToken);
            return;
        }

        var tunnelState = tunnelManager.GetTunnelState(node.WorkflowNodeId);
        if (tunnelState == TunnelState.Connected
            && node.LocalPort > 0
            && await ProbeLocalHealthAsync(node.LocalPort, cancellationToken))
        {
            await nodeRegistry.UpdateNodeHealthAsync(
                node.WorkflowNodeId,
                DesktopManagedNodeStatus.Connected,
                $"SSH tunnel active on 127.0.0.1:{node.LocalPort} - API health OK.",
                checkedAtUtc,
                healedAtUtc: checkedAtUtc,
                cancellationToken: cancellationToken);
            return;
        }

        DesktopNodeDiagnosticResult diagnostic;
        try
        {
            diagnostic = await sshProvisioningService.DiagnoseNodeAsync(node, cancellationToken);
        }
        catch (Exception ex)
        {
            await nodeRegistry.UpdateNodeHealthAsync(
                node.WorkflowNodeId,
                DesktopManagedNodeStatus.Error,
                $"SSH diagnostic failed: {ex.Message}",
                checkedAtUtc,
                cancellationToken: cancellationToken);
            return;
        }

        if (!diagnostic.IsSshReachable)
        {
            await nodeRegistry.UpdateNodeHealthAsync(
                node.WorkflowNodeId,
                DesktopManagedNodeStatus.ActionRequired,
                diagnostic.Detail,
                checkedAtUtc,
                cancellationToken: cancellationToken);
            return;
        }

        if (diagnostic.IsFleetManagerAgentActive || diagnostic.IsFleetManagerApiActive || diagnostic.IsDockerWorkerActive)
        {
            if (node.RemoteNodeId.HasValue && dashboardDataService.HasConfiguredBaseUrl)
            {
                var remoteWorkerStatus = await ProbeRemoteWorkerHeartbeatAsync(node.RemoteNodeId.Value, checkedAtUtc, cancellationToken);
                if (!remoteWorkerStatus.CanVerify || remoteWorkerStatus.IsHealthy)
                {
                    if (nodeRegistry.HasUsableCredentials(node)
                        && node.LocalPort > 0
                        && tunnelManager.GetTunnelState(node.WorkflowNodeId) != TunnelState.Connected)
                    {
                        await tunnelManager.OpenTunnelAsync(node, cancellationToken);
                    }

                    await nodeRegistry.UpdateNodeHealthAsync(
                        node.WorkflowNodeId,
                        DesktopManagedNodeStatus.RemoteReachable,
                        CombineDiagnosticDetails(diagnostic.Detail, remoteWorkerStatus.Detail),
                        checkedAtUtc,
                        sshSuccessAtUtc: checkedAtUtc,
                        cancellationToken: cancellationToken);
                    return;
                }

                recoveryReason = CombineDiagnosticDetails(
                    diagnostic.Detail,
                    $"{remoteWorkerStatus.Detail} Reinstalling and reconfiguring the worker automatically.");
            }
            else
            {
                if (nodeRegistry.HasUsableCredentials(node)
                    && node.LocalPort > 0
                    && tunnelManager.GetTunnelState(node.WorkflowNodeId) != TunnelState.Connected)
                {
                    await tunnelManager.OpenTunnelAsync(node, cancellationToken);
                }

                await nodeRegistry.UpdateNodeHealthAsync(
                    node.WorkflowNodeId,
                    DesktopManagedNodeStatus.RemoteReachable,
                    diagnostic.Detail,
                    checkedAtUtc,
                    sshSuccessAtUtc: checkedAtUtc,
                    cancellationToken: cancellationToken);
                return;
            }
        }

        if (!node.RemoteNodeId.HasValue)
        {
            await nodeRegistry.UpdateNodeHealthAsync(
                node.WorkflowNodeId,
                DesktopManagedNodeStatus.ActionRequired,
                "Remote node id is missing, so automatic worker recovery cannot continue.",
                checkedAtUtc,
                sshSuccessAtUtc: checkedAtUtc,
                cancellationToken: cancellationToken);
            return;
        }

        if (!dashboardDataService.HasConfiguredBaseUrl)
        {
            await nodeRegistry.UpdateNodeHealthAsync(
                node.WorkflowNodeId,
                DesktopManagedNodeStatus.ActionRequired,
                "A real API base URL is required before auto-healing can reinstall the worker.",
                checkedAtUtc,
                sshSuccessAtUtc: checkedAtUtc,
                cancellationToken: cancellationToken);
            return;
        }

        try
        {
            await nodeRegistry.UpdateNodeHealthAsync(
                node.WorkflowNodeId,
                DesktopManagedNodeStatus.Installing,
                recoveryReason,
                checkedAtUtc,
                sshSuccessAtUtc: checkedAtUtc,
                cancellationToken: cancellationToken);

            var request = nodeRegistry.BuildConnectionRequest(node);
            await sshProvisioningService.InstallAgentAsync(request, dashboardDataService.CurrentBaseUrl, progress: null, cancellationToken);
            await sshProvisioningService.ConfigureAgentAsync(request, node.RemoteNodeId.Value, dashboardDataService.CurrentBaseUrl, progress: null, cancellationToken);

            await nodeRegistry.UpdateNodeHealthAsync(
                node.WorkflowNodeId,
                DesktopManagedNodeStatus.Connected,
                "Worker was reinstalled and reconfigured successfully.",
                DateTime.UtcNow,
                sshSuccessAtUtc: checkedAtUtc,
                healedAtUtc: DateTime.UtcNow,
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            await nodeRegistry.UpdateNodeHealthAsync(
                node.WorkflowNodeId,
                DesktopManagedNodeStatus.Error,
                $"Auto-heal failed: {ex.Message}",
                DateTime.UtcNow,
                sshSuccessAtUtc: checkedAtUtc,
                cancellationToken: cancellationToken);
        }
    }

    private async Task<RemoteWorkerHeartbeatStatus> ProbeRemoteWorkerHeartbeatAsync(
        Guid remoteNodeId,
        DateTime checkedAtUtc,
        CancellationToken cancellationToken)
    {
        try
        {
            var remoteNode = await dashboardDataService.GetNodeAsync(remoteNodeId, cancellationToken);
            if (remoteNode is null)
            {
                return new RemoteWorkerHeartbeatStatus(
                    IsHealthy: false,
                    CanVerify: true,
                    Detail: "The API no longer has a record for this node.");
            }

            if (!remoteNode.LastHeartbeatAtUtc.HasValue)
            {
                return new RemoteWorkerHeartbeatStatus(
                    IsHealthy: false,
                    CanVerify: true,
                    Detail: "The worker has not reported any heartbeat to the API.");
            }

            var heartbeatAge = checkedAtUtc - remoteNode.LastHeartbeatAtUtc.Value;
            if (heartbeatAge > MaxWorkerHeartbeatAge)
            {
                return new RemoteWorkerHeartbeatStatus(
                    IsHealthy: false,
                    CanVerify: true,
                    Detail: $"The last worker heartbeat is stale ({heartbeatAge.TotalSeconds:0}s old).");
            }

            return new RemoteWorkerHeartbeatStatus(
                IsHealthy: true,
                CanVerify: true,
                Detail: $"API heartbeat OK ({heartbeatAge.TotalSeconds:0}s old).");
        }
        catch (Exception ex)
        {
            return new RemoteWorkerHeartbeatStatus(
                IsHealthy: false,
                CanVerify: false,
                Detail: $"Could not verify worker heartbeat from the API: {ex.Message}");
        }
    }

    private static string CombineDiagnosticDetails(string first, string second)
    {
        if (string.IsNullOrWhiteSpace(first))
        {
            return second;
        }

        if (string.IsNullOrWhiteSpace(second))
        {
            return first;
        }

        return $"{first} | {second}";
    }

    private async Task<bool> ProbeLocalHealthAsync(int localPort, CancellationToken cancellationToken)
    {
        foreach (var path in new[] { "api/health", "health" })
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{localPort}/{path}");
                var workerApiKey = Environment.GetEnvironmentVariable("FLEETMANAGER_AGENT_API_KEY");
                if (!string.IsNullOrWhiteSpace(workerApiKey))
                {
                    request.Headers.TryAddWithoutValidation("X-Api-Key", workerApiKey.Trim());
                }

                using var response = await _probeClient.SendAsync(request, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    return true;
                }
            }
            catch
            {
            }
        }

        return false;
    }

    private readonly record struct RemoteWorkerHeartbeatStatus(bool IsHealthy, bool CanVerify, string Detail);
}
