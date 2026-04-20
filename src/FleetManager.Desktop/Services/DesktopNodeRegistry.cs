using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using FleetManager.Contracts.Accounts;
using FleetManager.Contracts.Nodes;
using FleetManager.Desktop.Models;

namespace FleetManager.Desktop.Services;

public sealed class DesktopNodeRegistry : IDesktopNodeRegistry
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly SemaphoreSlim _syncLock = new(1, 1);

    private static string RegistryFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FleetManager",
        "desktop.nodes.json");

    public async Task<IReadOnlyList<DesktopManagedNodeRecord>> GetNodesAsync(CancellationToken cancellationToken = default)
    {
        await _syncLock.WaitAsync(cancellationToken);
        try
        {
            var snapshot = await LoadSnapshotUnsafeAsync(cancellationToken);
            return snapshot.Nodes
                .OrderBy(node => node.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(node => node.CurrentIp, StringComparer.OrdinalIgnoreCase)
                .Select(Clone)
                .ToList();
        }
        finally
        {
            _syncLock.Release();
        }
    }

    public async Task<DesktopManagedNodeRecord?> GetByRemoteNodeIdAsync(Guid remoteNodeId, CancellationToken cancellationToken = default)
    {
        await _syncLock.WaitAsync(cancellationToken);
        try
        {
            var snapshot = await LoadSnapshotUnsafeAsync(cancellationToken);
            var node = snapshot.Nodes.FirstOrDefault(candidate => candidate.RemoteNodeId == remoteNodeId);
            return node is null ? null : Clone(node);
        }
        finally
        {
            _syncLock.Release();
        }
    }

    public async Task<DesktopManagedNodeRecord?> GetByWorkflowNodeIdAsync(Guid workflowNodeId, CancellationToken cancellationToken = default)
    {
        await _syncLock.WaitAsync(cancellationToken);
        try
        {
            var snapshot = await LoadSnapshotUnsafeAsync(cancellationToken);
            var node = snapshot.Nodes.FirstOrDefault(candidate => candidate.WorkflowNodeId == workflowNodeId);
            return node is null ? null : Clone(node);
        }
        finally
        {
            _syncLock.Release();
        }
    }

    public async Task<DesktopManagedNodeRecord> UpsertProvisionedNodeAsync(CreateNodeRequest request, NodeSummaryResponse remoteNode, string? apiBaseUrl, CancellationToken cancellationToken = default)
    {
        await _syncLock.WaitAsync(cancellationToken);
        try
        {
            var snapshot = await LoadSnapshotUnsafeAsync(cancellationToken);
            var managedNode = FindExistingNode(snapshot, remoteNode.Id, request.IpAddress)
                ?? new DesktopManagedNodeRecord
                {
                    WorkflowNodeId = Guid.NewGuid(),
                    LocalPort = GetNextLocalPort(snapshot.Nodes)
                };

            ApplyRemoteNode(managedNode, remoteNode, apiBaseUrl);
            ApplyConnectionRequest(managedNode, request);
            managedNode.Status = DesktopManagedNodeStatus.Installing;
            managedNode.StatusMessage = "Desktop registered the node and queued it for healing/provisioning.";
            managedNode.LastHealthCheckAtUtc = DateTime.UtcNow;

            Upsert(snapshot.Nodes, managedNode);
            await SaveSnapshotUnsafeAsync(snapshot, cancellationToken);
            return Clone(managedNode);
        }
        finally
        {
            _syncLock.Release();
        }
    }

    public async Task SyncRemoteNodesAsync(IReadOnlyList<NodeSummaryResponse> remoteNodes, string? apiBaseUrl, CancellationToken cancellationToken = default)
    {
        await _syncLock.WaitAsync(cancellationToken);
        try
        {
            var snapshot = await LoadSnapshotUnsafeAsync(cancellationToken);
            foreach (var remoteNode in remoteNodes)
            {
                var managedNode = FindExistingNode(snapshot, remoteNode.Id, remoteNode.IpAddress)
                    ?? new DesktopManagedNodeRecord
                    {
                        WorkflowNodeId = Guid.NewGuid(),
                        LocalPort = GetNextLocalPort(snapshot.Nodes)
                    };

                ApplyRemoteNode(managedNode, remoteNode, apiBaseUrl);
                Upsert(snapshot.Nodes, managedNode);
            }

            await SaveSnapshotUnsafeAsync(snapshot, cancellationToken);
        }
        finally
        {
            _syncLock.Release();
        }
    }

    public async Task SyncTaskDataAsync(IReadOnlyList<AccountSummaryResponse> accounts, CancellationToken cancellationToken = default)
    {
        await _syncLock.WaitAsync(cancellationToken);
        try
        {
            var snapshot = await LoadSnapshotUnsafeAsync(cancellationToken);
            var accountsByNodeId = accounts
                .GroupBy(account => account.NodeId)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .OrderBy(account => account.Email, StringComparer.OrdinalIgnoreCase)
                        .Select(account => new DesktopManagedAccountTask
                        {
                            AccountId = account.Id,
                            Email = account.Email,
                            Username = account.Username,
                            Status = account.Status,
                            CurrentStage = account.CurrentStage,
                            ActiveAlertTitle = account.ActiveAlertTitle,
                            ActiveAlertMessage = account.ActiveAlertMessage,
                            CurrentProxyIndex = account.CurrentProxyIndex,
                            ProxyCount = account.ProxyCount
                        })
                        .ToList());

            foreach (var node in snapshot.Nodes)
            {
                if (node.RemoteNodeId.HasValue && accountsByNodeId.TryGetValue(node.RemoteNodeId.Value, out var nodeTasks))
                {
                    node.TaskData = nodeTasks;
                    node.LastTaskSyncAtUtc = DateTime.UtcNow;
                    continue;
                }

                node.TaskData = new List<DesktopManagedAccountTask>();
                node.LastTaskSyncAtUtc = DateTime.UtcNow;
            }

            await SaveSnapshotUnsafeAsync(snapshot, cancellationToken);
        }
        finally
        {
            _syncLock.Release();
        }
    }

    public async Task<bool> RemoveByRemoteNodeIdAsync(Guid remoteNodeId, CancellationToken cancellationToken = default)
    {
        await _syncLock.WaitAsync(cancellationToken);
        try
        {
            var snapshot = await LoadSnapshotUnsafeAsync(cancellationToken);
            var removed = snapshot.Nodes.RemoveAll(node => node.RemoteNodeId == remoteNodeId) > 0;
            if (removed)
            {
                await SaveSnapshotUnsafeAsync(snapshot, cancellationToken);
            }

            return removed;
        }
        finally
        {
            _syncLock.Release();
        }
    }

    public async Task UpdateNodeHealthAsync(Guid workflowNodeId, DesktopManagedNodeStatus status, string? statusMessage, DateTime checkedAtUtc, DateTime? sshSuccessAtUtc = null, DateTime? healedAtUtc = null, CancellationToken cancellationToken = default)
    {
        await _syncLock.WaitAsync(cancellationToken);
        try
        {
            var snapshot = await LoadSnapshotUnsafeAsync(cancellationToken);
            var node = snapshot.Nodes.FirstOrDefault(candidate => candidate.WorkflowNodeId == workflowNodeId);
            if (node is null)
            {
                return;
            }

            node.Status = status;
            node.StatusMessage = statusMessage;
            node.LastHealthCheckAtUtc = checkedAtUtc;
            if (sshSuccessAtUtc.HasValue)
            {
                node.LastSshSuccessAtUtc = sshSuccessAtUtc.Value;
            }

            if (healedAtUtc.HasValue)
            {
                node.LastHealedAtUtc = healedAtUtc.Value;
            }

            await SaveSnapshotUnsafeAsync(snapshot, cancellationToken);
        }
        finally
        {
            _syncLock.Release();
        }
    }

    public async Task<DesktopManagedNodeRecord> UpdateNodeConnectionAsync(
        Guid workflowNodeId,
        string name,
        string currentIp,
        int sshPort,
        string sshUsername,
        string? sshPassword,
        string? sshPrivateKey,
        string authType,
        int controlPort,
        string? region,
        CancellationToken cancellationToken = default)
    {
        await _syncLock.WaitAsync(cancellationToken);
        try
        {
            var snapshot = await LoadSnapshotUnsafeAsync(cancellationToken);
            var node = snapshot.Nodes.FirstOrDefault(candidate => candidate.WorkflowNodeId == workflowNodeId)
                ?? throw new InvalidOperationException($"Node with WorkflowNodeId {workflowNodeId} was not found in the desktop registry.");

            node.Name = name.Trim();
            node.CurrentIp = currentIp.Trim();
            node.SshPort = sshPort;
            node.SshUsername = sshUsername.Trim();
            node.AuthType = authType.Trim();
            node.ControlPort = controlPort;
            node.Region = string.IsNullOrWhiteSpace(region) ? null : region.Trim();
            ApplyStoredCredentials(node, sshPassword, sshPrivateKey);

            await SaveSnapshotUnsafeAsync(snapshot, cancellationToken);
            return Clone(node);
        }
        finally
        {
            _syncLock.Release();
        }
    }

    public async Task<bool> RemoveByWorkflowNodeIdAsync(Guid workflowNodeId, CancellationToken cancellationToken = default)
    {
        await _syncLock.WaitAsync(cancellationToken);
        try
        {
            var snapshot = await LoadSnapshotUnsafeAsync(cancellationToken);
            var removed = snapshot.Nodes.RemoveAll(node => node.WorkflowNodeId == workflowNodeId) > 0;
            if (removed)
            {
                await SaveSnapshotUnsafeAsync(snapshot, cancellationToken);
            }

            return removed;
        }
        finally
        {
            _syncLock.Release();
        }
    }

    public CreateNodeRequest BuildConnectionRequest(DesktopManagedNodeRecord node)
    {
        if (!node.HasStoredCredentials)
        {
            throw new InvalidOperationException($"Node '{node.Name}' does not have stored SSH credentials in the desktop registry.");
        }

        return new CreateNodeRequest
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
    }

    private static DesktopManagedNodeRecord? FindExistingNode(DesktopNodeRegistrySnapshot snapshot, Guid remoteNodeId, string? currentIp)
        => snapshot.Nodes.FirstOrDefault(node => node.RemoteNodeId == remoteNodeId)
            ?? snapshot.Nodes.FirstOrDefault(node =>
                !string.IsNullOrWhiteSpace(currentIp)
                && string.Equals(node.CurrentIp, currentIp.Trim(), StringComparison.OrdinalIgnoreCase));

    private static void ApplyRemoteNode(DesktopManagedNodeRecord target, NodeSummaryResponse remoteNode, string? apiBaseUrl)
    {
        target.RemoteNodeId = remoteNode.Id;
        target.Name = remoteNode.Name;
        target.CurrentIp = remoteNode.IpAddress;
        target.SshPort = remoteNode.SshPort;
        target.SshUsername = remoteNode.SshUsername;
        target.ControlPort = remoteNode.ControlPort;
        target.OsType = remoteNode.OsType;
        target.Region = remoteNode.Region;
        target.LastKnownApiBaseUrl = NormalizeApiBaseUrl(apiBaseUrl);
        if (!DesktopEnvironment.ShouldPersistSshCredentials())
        {
            target.EncryptedSshPassword = null;
            target.EncryptedSshPrivateKey = null;
        }
    }

    private static void ApplyConnectionRequest(DesktopManagedNodeRecord target, CreateNodeRequest request)
    {
        target.CurrentIp = request.IpAddress.Trim();
        target.SshPort = request.SshPort;
        target.SshUsername = request.SshUsername.Trim();
        target.AuthType = request.AuthType.Trim();
        target.ControlPort = request.ControlPort;
        target.OsType = request.OsType.Trim();
        target.Region = string.IsNullOrWhiteSpace(request.Region) ? null : request.Region.Trim();
        ApplyStoredCredentials(target, request.SshPassword, request.SshPrivateKey);
    }

    private static void ApplyStoredCredentials(DesktopManagedNodeRecord target, string? sshPassword, string? sshPrivateKey)
    {
        if (!DesktopEnvironment.ShouldPersistSshCredentials())
        {
            target.EncryptedSshPassword = null;
            target.EncryptedSshPrivateKey = null;
            return;
        }

        target.EncryptedSshPassword = DesktopCredentialProtector.Protect(sshPassword);
        target.EncryptedSshPrivateKey = DesktopCredentialProtector.Protect(sshPrivateKey);
    }

    private static void Upsert(List<DesktopManagedNodeRecord> nodes, DesktopManagedNodeRecord candidate)
    {
        var existingIndex = nodes.FindIndex(node => node.WorkflowNodeId == candidate.WorkflowNodeId);
        if (existingIndex >= 0)
        {
            nodes[existingIndex] = candidate;
            return;
        }

        nodes.Add(candidate);
    }

    private static int GetNextLocalPort(IEnumerable<DesktopManagedNodeRecord> nodes)
    {
        const int startingPort = 8080;
        var usedPorts = nodes
            .Select(node => node.LocalPort)
            .Where(port => port > 0)
            .ToHashSet();

        var candidate = startingPort;
        while (usedPorts.Contains(candidate))
        {
            candidate++;
        }

        return candidate;
    }

    private static string? NormalizeApiBaseUrl(string? apiBaseUrl)
    {
        if (string.IsNullOrWhiteSpace(apiBaseUrl))
        {
            return null;
        }

        return apiBaseUrl.EndsWith("/", StringComparison.Ordinal) ? apiBaseUrl : $"{apiBaseUrl}/";
    }

    private static DesktopManagedNodeRecord Clone(DesktopManagedNodeRecord node)
        => new()
        {
            WorkflowNodeId = node.WorkflowNodeId,
            RemoteNodeId = node.RemoteNodeId,
            Name = node.Name,
            CurrentIp = node.CurrentIp,
            SshPort = node.SshPort,
            SshUsername = node.SshUsername,
            AuthType = node.AuthType,
            EncryptedSshPassword = node.EncryptedSshPassword,
            EncryptedSshPrivateKey = node.EncryptedSshPrivateKey,
            LocalPort = node.LocalPort,
            ControlPort = node.ControlPort,
            OsType = node.OsType,
            Region = node.Region,
            Status = node.Status,
            StatusMessage = node.StatusMessage,
            LastKnownApiBaseUrl = node.LastKnownApiBaseUrl,
            LastHealthCheckAtUtc = node.LastHealthCheckAtUtc,
            LastSshSuccessAtUtc = node.LastSshSuccessAtUtc,
            LastHealedAtUtc = node.LastHealedAtUtc,
            LastTaskSyncAtUtc = node.LastTaskSyncAtUtc,
            TaskData = node.TaskData
                .Select(task => new DesktopManagedAccountTask
                {
                    AccountId = task.AccountId,
                    Email = task.Email,
                    Username = task.Username,
                    Status = task.Status,
                    CurrentStage = task.CurrentStage,
                    ActiveAlertTitle = task.ActiveAlertTitle,
                    ActiveAlertMessage = task.ActiveAlertMessage,
                    CurrentProxyIndex = task.CurrentProxyIndex,
                    ProxyCount = task.ProxyCount
                })
                .ToList()
        };

    private static async Task<DesktopNodeRegistrySnapshot> LoadSnapshotUnsafeAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(RegistryFilePath))
        {
            return new DesktopNodeRegistrySnapshot();
        }

        var json = await File.ReadAllTextAsync(RegistryFilePath, cancellationToken);
        return JsonSerializer.Deserialize<DesktopNodeRegistrySnapshot>(json, JsonOptions)
            ?? new DesktopNodeRegistrySnapshot();
    }

    private static async Task SaveSnapshotUnsafeAsync(DesktopNodeRegistrySnapshot snapshot, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(RegistryFilePath)
            ?? throw new InvalidOperationException("Desktop node registry directory could not be resolved.");
        Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(snapshot, JsonOptions);
        await File.WriteAllTextAsync(RegistryFilePath, json, cancellationToken);
    }
}
