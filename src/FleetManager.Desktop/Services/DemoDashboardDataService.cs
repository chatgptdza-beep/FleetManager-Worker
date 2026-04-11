using System.Text.Json;
using FleetManager.Contracts.Accounts;
using FleetManager.Contracts.Nodes;

namespace FleetManager.Desktop.Services;

public sealed class DemoDashboardDataService : IDashboardDataService
{
    private readonly List<NodeSummaryResponse> _nodes;
    private readonly List<AccountStageAlertDetailsResponse> _accountDetails;
    private readonly Dictionary<Guid, NodeCommandStatusResponse> _commands;

    public DemoDashboardDataService()
    {
        _nodes = BuildNodes();
        _accountDetails = BuildDetails();
        _commands = new Dictionary<Guid, NodeCommandStatusResponse>();
        RecalculateNodeCounts();
    }

    public string CurrentModeLabel => "Demo mode";
    public string CurrentBaseUrl { get; private set; } = "http://82.223.9.98:5000/";

    public void ConfigureBaseUrl(string baseUrl)
    {
        CurrentBaseUrl = baseUrl.EndsWith("/", StringComparison.Ordinal) ? baseUrl : $"{baseUrl}/";
    }

    public Task<IReadOnlyList<NodeSummaryResponse>> GetNodesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<NodeSummaryResponse>>(_nodes.Select(CloneNode).ToList());

    public Task<NodeSummaryResponse> CreateNodeAsync(CreateNodeRequest request, CancellationToken cancellationToken = default)
    {
        var created = new NodeSummaryResponse
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            IpAddress = request.IpAddress.Trim(),
            SshPort = request.SshPort,
            SshUsername = request.SshUsername.Trim(),
            AuthType = request.AuthType.Trim(),
            OsType = request.OsType.Trim(),
            Region = request.Region,
            Status = "Pending",
            CpuPercent = 0,
            RamPercent = 0,
            DiskPercent = 0,
            RamUsedGb = 0,
            StorageUsedGb = 0,
            PingMs = 0,
            ActiveSessions = 0,
            ControlPort = request.ControlPort,
            ConnectionState = "Pending",
            ConnectionTimeoutSeconds = 0,
            AssignedAccountCount = 0,
            RunningAccounts = 0,
            ManualAccounts = 0,
            AlertAccounts = 0,
            AgentVersion = "1.0.0",
            LastHeartbeatAtUtc = null
        };

        _nodes.Add(created);
        return Task.FromResult(CloneNode(created));
    }

    public Task<IReadOnlyList<AccountSummaryResponse>> GetAccountsAsync(Guid? nodeId = null, CancellationToken cancellationToken = default)
    {
        var accounts = _accountDetails.Select(MapSummary);
        if (nodeId.HasValue)
        {
            accounts = accounts.Where(account => account.NodeId == nodeId.Value);
        }

        return Task.FromResult<IReadOnlyList<AccountSummaryResponse>>(accounts.OrderBy(account => account.Email).ToList());
    }

    public Task<AccountStageAlertDetailsResponse?> GetAccountStageAlertsAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        var details = _accountDetails.FirstOrDefault(account => account.AccountId == accountId);
        return Task.FromResult(details is null ? null : CloneDetails(details));
    }

    public Task<AccountSummaryResponse> CreateAccountAsync(CreateAccountRequest request, CancellationToken cancellationToken = default)
    {
        var node = _nodes.FirstOrDefault(x => x.Id == request.NodeId)
            ?? throw new InvalidOperationException("Node not found.");

        var created = new AccountStageAlertDetailsResponse
        {
            AccountId = Guid.NewGuid(),
            Email = request.Email.Trim(),
            Username = request.Username.Trim(),
            Status = request.Status.Trim(),
            NodeId = node.Id,
            NodeName = node.Name,
            NodeIpAddress = node.IpAddress,
            CurrentStage = "Ready"
        };

        _accountDetails.Add(created);
        RecalculateNodeCounts();
        return Task.FromResult(MapSummary(created));
    }

    public Task<AccountSummaryResponse?> UpdateAccountAsync(Guid accountId, UpdateAccountRequest request, CancellationToken cancellationToken = default)
    {
        var account = _accountDetails.FirstOrDefault(x => x.AccountId == accountId);
        if (account is null)
        {
            return Task.FromResult<AccountSummaryResponse?>(null);
        }

        account.Email = request.Email.Trim();
        account.Username = request.Username.Trim();
        account.Status = request.Status.Trim();
        RecalculateNodeCounts();
        return Task.FromResult<AccountSummaryResponse?>(MapSummary(account));
    }

    public Task<bool> DeleteAccountAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        var removed = _accountDetails.RemoveAll(x => x.AccountId == accountId) > 0;
        if (removed)
        {
            RecalculateNodeCounts();
        }

        return Task.FromResult(removed);
    }

    public Task<Guid?> DispatchNodeCommandAsync(Guid nodeId, DispatchNodeCommandRequest request, CancellationToken cancellationToken = default)
    {
        var node = _nodes.FirstOrDefault(x => x.Id == nodeId);
        if (node is null)
        {
            return Task.FromResult<Guid?>(null);
        }

        var payload = ParsePayload(request.PayloadJson);
        var account = payload?.AccountId is null
            ? null
            : _accountDetails.FirstOrDefault(x => x.AccountId == payload.AccountId.Value);

        if (account is not null)
        {
            ApplyCommand(account, request.CommandType);
        }

        var commandId = Guid.NewGuid();
        _commands[commandId] = new NodeCommandStatusResponse
        {
            CommandId = commandId,
            NodeId = nodeId,
            CommandType = request.CommandType,
            Status = "Executed",
            ResultMessage = BuildCommandResultMessage(node, account, request.CommandType),
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            ExecutedAtUtc = DateTime.UtcNow
        };

        node.ConnectionState = request.CommandType switch
        {
            "OpenAssignedSession" => "Connected",
            "BringManagedWindowToFront" => "Connected",
            "StartBrowser" => "Connected",
            "LoginWorkflow" => "Connected",
            "StartAutomation" => "Connected",
            _ => node.ConnectionState
        };

        RecalculateNodeCounts();
        return Task.FromResult<Guid?>(commandId);
    }

    public Task<NodeCommandStatusResponse?> GetNodeCommandStatusAsync(Guid nodeId, Guid commandId, CancellationToken cancellationToken = default)
    {
        if (_commands.TryGetValue(commandId, out var status) && status.NodeId == nodeId)
        {
            return Task.FromResult<NodeCommandStatusResponse?>(CloneCommandStatus(status));
        }

        return Task.FromResult<NodeCommandStatusResponse?>(null);
    }

    private void ApplyCommand(AccountStageAlertDetailsResponse account, string commandType)
    {
        switch (commandType)
        {
            case "StartBrowser":
                account.Status = "Running";
                account.CurrentStage = "Slot Search";
                account.ActiveAlertSeverity = null;
                account.ActiveAlertStage = null;
                account.ActiveAlertTitle = null;
                account.ActiveAlertMessage = null;
                break;
            case "OpenAssignedSession":
            case "BringManagedWindowToFront":
                account.Status = "Manual";
                if (string.IsNullOrWhiteSpace(account.ActiveAlertSeverity))
                {
                    account.ActiveAlertSeverity = "ManualRequired";
                    account.ActiveAlertStage = "Manual Review";
                    account.ActiveAlertTitle = "Browser surfaced for operator";
                    account.ActiveAlertMessage = "The session is ready for remote review.";
                }
                break;
            case "StopBrowser":
                account.Status = "Paused";
                account.CurrentStage = "Browser Stopped";
                account.ActiveAlertSeverity = null;
                account.ActiveAlertStage = null;
                account.ActiveAlertTitle = null;
                account.ActiveAlertMessage = "Browser stopped by operator command.";
                break;
            case "LoginWorkflow":
                account.Status = "Running";
                account.CurrentStage = "Login";
                account.ActiveAlertSeverity = null;
                account.ActiveAlertStage = null;
                account.ActiveAlertTitle = null;
                account.ActiveAlertMessage = "Login sequence queued.";
                break;
            case "StartAutomation":
                account.Status = "Running";
                account.CurrentStage = "Automation Active";
                account.ActiveAlertSeverity = null;
                account.ActiveAlertStage = null;
                account.ActiveAlertTitle = null;
                account.ActiveAlertMessage = "Auto workflow resumed.";
                break;
            case "StopAutomation":
                account.Status = "Paused";
                account.CurrentStage = "Automation Stopped";
                account.ActiveAlertSeverity = null;
                account.ActiveAlertStage = null;
                account.ActiveAlertTitle = "Automation stopped";
                account.ActiveAlertMessage = "Auto workflow stopped from the dashboard.";
                break;
            case "PauseAutomation":
                account.Status = "Paused";
                account.CurrentStage = "Automation Paused";
                account.ActiveAlertSeverity = null;
                account.ActiveAlertStage = null;
                account.ActiveAlertTitle = "Automation paused";
                account.ActiveAlertMessage = "Auto workflow paused from the dashboard.";
                break;
            case "FetchSessionLogs":
                account.ActiveAlertMessage = account.ActiveAlertMessage ?? "Logs prepared for export.";
                break;
        }
    }

    private void RecalculateNodeCounts()
    {
        foreach (var node in _nodes)
        {
            var accounts = _accountDetails.Where(account => account.NodeId == node.Id).ToList();
            node.AssignedAccountCount = accounts.Count;
            node.RunningAccounts = accounts.Count(account => account.Status is "Running" or "Stable");
            node.ManualAccounts = accounts.Count(account => account.Status == "Manual");
            node.AlertAccounts = accounts.Count(account => !string.IsNullOrWhiteSpace(account.ActiveAlertTitle));
            node.ActiveSessions = Math.Max(node.RunningAccounts, accounts.Count(account => account.Status == "Manual"));
        }
    }

    private static CommandPayload? ParsePayload(string payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<CommandPayload>(payloadJson);
        }
        catch
        {
            return null;
        }
    }

    private static AccountSummaryResponse MapSummary(AccountStageAlertDetailsResponse account) => new()
    {
        Id = account.AccountId,
        Email = account.Email,
        Username = account.Username,
        Status = account.Status,
        NodeId = account.NodeId,
        NodeName = account.NodeName,
        NodeIpAddress = account.NodeIpAddress,
        CurrentStage = account.CurrentStage,
        ActiveAlertSeverity = account.ActiveAlertSeverity,
        ActiveAlertStage = account.ActiveAlertStage,
        ActiveAlertTitle = account.ActiveAlertTitle,
        ActiveAlertMessage = account.ActiveAlertMessage
    };

    private static NodeSummaryResponse CloneNode(NodeSummaryResponse source) => new()
    {
        Id = source.Id,
        Name = source.Name,
        IpAddress = source.IpAddress,
        SshPort = source.SshPort,
        SshUsername = source.SshUsername,
        AuthType = source.AuthType,
        OsType = source.OsType,
        Region = source.Region,
        Status = source.Status,
        LastHeartbeatAtUtc = source.LastHeartbeatAtUtc,
        CpuPercent = source.CpuPercent,
        RamPercent = source.RamPercent,
        DiskPercent = source.DiskPercent,
        RamUsedGb = source.RamUsedGb,
        StorageUsedGb = source.StorageUsedGb,
        PingMs = source.PingMs,
        ActiveSessions = source.ActiveSessions,
        ControlPort = source.ControlPort,
        ConnectionState = source.ConnectionState,
        ConnectionTimeoutSeconds = source.ConnectionTimeoutSeconds,
        AssignedAccountCount = source.AssignedAccountCount,
        RunningAccounts = source.RunningAccounts,
        ManualAccounts = source.ManualAccounts,
        AlertAccounts = source.AlertAccounts,
        AgentVersion = source.AgentVersion
    };

    private static NodeCommandStatusResponse CloneCommandStatus(NodeCommandStatusResponse source) => new()
    {
        CommandId = source.CommandId,
        NodeId = source.NodeId,
        CommandType = source.CommandType,
        Status = source.Status,
        ResultMessage = source.ResultMessage,
        CreatedAtUtc = source.CreatedAtUtc,
        UpdatedAtUtc = source.UpdatedAtUtc,
        ExecutedAtUtc = source.ExecutedAtUtc
    };

    private static AccountStageAlertDetailsResponse CloneDetails(AccountStageAlertDetailsResponse source) => new()
    {
        AccountId = source.AccountId,
        Email = source.Email,
        Username = source.Username,
        Status = source.Status,
        NodeId = source.NodeId,
        NodeName = source.NodeName,
        NodeIpAddress = source.NodeIpAddress,
        CurrentStage = source.CurrentStage,
        ActiveAlertSeverity = source.ActiveAlertSeverity,
        ActiveAlertStage = source.ActiveAlertStage,
        ActiveAlertTitle = source.ActiveAlertTitle,
        ActiveAlertMessage = source.ActiveAlertMessage,
        Stages = source.Stages
            .Select(stage => new AccountWorkflowStageResponse
            {
                StageCode = stage.StageCode,
                StageName = stage.StageName,
                State = stage.State,
                Message = stage.Message,
                OccurredAtUtc = stage.OccurredAtUtc
            })
            .ToList()
    };

    private static List<NodeSummaryResponse> BuildNodes()
        => new()
        {
            new NodeSummaryResponse
            {
                Id = Guid.Parse("3a5ff57d-e3d8-4d04-858e-fcef5b4997bf"),
                Name = "VPS-PAR-01",
                IpAddress = "10.0.0.21",
                SshPort = 22,
                SshUsername = "deploy",
                AuthType = "SshKey",
                OsType = "Ubuntu 24.04",
                Region = "Paris",
                Status = "Online",
                CpuPercent = 28,
                RamPercent = 61,
                DiskPercent = 47,
                RamUsedGb = 14,
                StorageUsedGb = 182,
                PingMs = 103,
                ActiveSessions = 1,
                ControlPort = 9001,
                ConnectionState = "Connected",
                ConnectionTimeoutSeconds = 5,
                AssignedAccountCount = 1,
                RunningAccounts = 1,
                ManualAccounts = 0,
                AlertAccounts = 1,
                AgentVersion = "1.0.0",
                LastHeartbeatAtUtc = DateTime.UtcNow.AddSeconds(-18)
            },
            new NodeSummaryResponse
            {
                Id = Guid.Parse("df8ec7ab-4bd8-43c8-bd6d-4b5ebf901437"),
                Name = "VPS-FRA-03",
                IpAddress = "10.0.0.37",
                SshPort = 22,
                SshUsername = "deploy",
                AuthType = "SshKey",
                OsType = "Ubuntu 24.04",
                Region = "Frankfurt",
                Status = "Degraded",
                CpuPercent = 74,
                RamPercent = 68,
                DiskPercent = 58,
                RamUsedGb = 18,
                StorageUsedGb = 241,
                PingMs = 129,
                ActiveSessions = 1,
                ControlPort = 9002,
                ConnectionState = "Reconnecting",
                ConnectionTimeoutSeconds = 8,
                AssignedAccountCount = 1,
                RunningAccounts = 0,
                ManualAccounts = 1,
                AlertAccounts = 1,
                AgentVersion = "1.0.0",
                LastHeartbeatAtUtc = DateTime.UtcNow.AddSeconds(-31)
            },
            new NodeSummaryResponse
            {
                Id = Guid.Parse("70c8c145-a615-42eb-82bf-b93112f0fe12"),
                Name = "VPS-MAD-02",
                IpAddress = "10.0.0.52",
                SshPort = 22,
                SshUsername = "deploy",
                AuthType = "SshKey",
                OsType = "Ubuntu 24.04",
                Region = "Madrid",
                Status = "Online",
                CpuPercent = 19,
                RamPercent = 44,
                DiskPercent = 39,
                RamUsedGb = 13,
                StorageUsedGb = 169,
                PingMs = 116,
                ActiveSessions = 1,
                ControlPort = 9003,
                ConnectionState = "Connected",
                ConnectionTimeoutSeconds = 5,
                AssignedAccountCount = 1,
                RunningAccounts = 1,
                ManualAccounts = 0,
                AlertAccounts = 0,
                AgentVersion = "1.0.0",
                LastHeartbeatAtUtc = DateTime.UtcNow.AddSeconds(-12)
            },
            new NodeSummaryResponse
            {
                Id = Guid.Parse("2f4a72af-4f7b-4c51-a3cb-b0ad6e3b3ecf"),
                Name = "VPS-LAG-04",
                IpAddress = "10.0.0.68",
                SshPort = 22,
                SshUsername = "deploy",
                AuthType = "SshKey",
                OsType = "Ubuntu 24.04",
                Region = "Lagos",
                Status = "Degraded",
                CpuPercent = 81,
                RamPercent = 72,
                DiskPercent = 63,
                RamUsedGb = 21,
                StorageUsedGb = 254,
                PingMs = 135,
                ActiveSessions = 1,
                ControlPort = 9004,
                ConnectionState = "Degraded",
                ConnectionTimeoutSeconds = 9,
                AssignedAccountCount = 1,
                RunningAccounts = 0,
                ManualAccounts = 0,
                AlertAccounts = 1,
                AgentVersion = "1.0.0",
                LastHeartbeatAtUtc = DateTime.UtcNow.AddSeconds(-27)
            }
        };

    private static List<AccountStageAlertDetailsResponse> BuildDetails()
        => new()
        {
            new AccountStageAlertDetailsResponse
            {
                AccountId = Guid.Parse("f58b7535-64fc-4569-bd57-c5eecc357f40"),
                Email = "booking.alpha@example.com",
                Username = "booking.alpha",
                Status = "Running",
                NodeId = Guid.Parse("3a5ff57d-e3d8-4d04-858e-fcef5b4997bf"),
                NodeName = "VPS-PAR-01",
                NodeIpAddress = "10.0.0.21",
                CurrentStage = "Proxy Check",
                ActiveAlertSeverity = "Warning",
                ActiveAlertStage = "Proxy Check",
                ActiveAlertTitle = "Latency spike detected",
                ActiveAlertMessage = "Proxy validation exceeded the 4 second threshold. Keep the account under watch before auto-retry.",
                Stages = new List<AccountWorkflowStageResponse>
                {
                    new() { StageCode = "login", StageName = "Login", State = "Completed", Message = "Credentials accepted.", OccurredAtUtc = DateTime.UtcNow.AddMinutes(-12) },
                    new() { StageCode = "proxy_check", StageName = "Proxy Check", State = "Warning", Message = "Latency reached 4200 ms.", OccurredAtUtc = DateTime.UtcNow.AddMinutes(-5) },
                    new() { StageCode = "slot_search", StageName = "Slot Search", State = "Running", Message = "Watching appointment inventory.", OccurredAtUtc = DateTime.UtcNow.AddMinutes(-1) },
                    new() { StageCode = "payment", StageName = "Payment", State = "Pending", Message = "Not started yet.", OccurredAtUtc = null }
                }
            },
            new AccountStageAlertDetailsResponse
            {
                AccountId = Guid.Parse("2bc0f75d-5df9-4fe9-96a0-67f8c4d8dc72"),
                Email = "booking.bravo@example.com",
                Username = "booking.bravo",
                Status = "Manual",
                NodeId = Guid.Parse("df8ec7ab-4bd8-43c8-bd6d-4b5ebf901437"),
                NodeName = "VPS-FRA-03",
                NodeIpAddress = "10.0.0.37",
                CurrentStage = "Captcha Solve",
                ActiveAlertSeverity = "Critical",
                ActiveAlertStage = "Captcha Solve",
                ActiveAlertTitle = "Stage failed after retries",
                ActiveAlertMessage = "Captcha provider timed out three times. Manual review is required before the workflow can continue.",
                Stages = new List<AccountWorkflowStageResponse>
                {
                    new() { StageCode = "login", StageName = "Login", State = "Completed", Message = "Login completed successfully.", OccurredAtUtc = DateTime.UtcNow.AddMinutes(-21) },
                    new() { StageCode = "profile_sync", StageName = "Profile Sync", State = "Completed", Message = "Profile data synced.", OccurredAtUtc = DateTime.UtcNow.AddMinutes(-19) },
                    new() { StageCode = "captcha_solve", StageName = "Captcha Solve", State = "Failed", Message = "External solver timeout on three attempts.", OccurredAtUtc = DateTime.UtcNow.AddMinutes(-4) },
                    new() { StageCode = "manual_review", StageName = "Manual Review", State = "ManualRequired", Message = "Operator acknowledgement pending.", OccurredAtUtc = DateTime.UtcNow.AddMinutes(-3) }
                }
            },
            new AccountStageAlertDetailsResponse
            {
                AccountId = Guid.Parse("8eaa4352-f5a4-49de-9218-25a624cf96af"),
                Email = "booking.charlie@example.com",
                Username = "booking.charlie",
                Status = "Stable",
                NodeId = Guid.Parse("70c8c145-a615-42eb-82bf-b93112f0fe12"),
                NodeName = "VPS-MAD-02",
                NodeIpAddress = "10.0.0.52",
                CurrentStage = "Slot Search",
                Stages = new List<AccountWorkflowStageResponse>
                {
                    new() { StageCode = "login", StageName = "Login", State = "Completed", Message = "Login completed.", OccurredAtUtc = DateTime.UtcNow.AddMinutes(-16) },
                    new() { StageCode = "proxy_check", StageName = "Proxy Check", State = "Completed", Message = "Proxy healthy.", OccurredAtUtc = DateTime.UtcNow.AddMinutes(-14) },
                    new() { StageCode = "slot_search", StageName = "Slot Search", State = "Running", Message = "No stage issues detected.", OccurredAtUtc = DateTime.UtcNow.AddMinutes(-1) },
                    new() { StageCode = "payment", StageName = "Payment", State = "Pending", Message = "Waiting for slot match.", OccurredAtUtc = null }
                }
            },
            new AccountStageAlertDetailsResponse
            {
                AccountId = Guid.Parse("ab0cba77-63a0-4fef-bca0-738f08d2dc55"),
                Email = "booking.delta@example.com",
                Username = "booking.delta",
                Status = "Paused",
                NodeId = Guid.Parse("2f4a72af-4f7b-4c51-a3cb-b0ad6e3b3ecf"),
                NodeName = "VPS-LAG-04",
                NodeIpAddress = "10.0.0.68",
                CurrentStage = "Manual Review",
                ActiveAlertSeverity = "ManualRequired",
                ActiveAlertStage = "Manual Review",
                ActiveAlertTitle = "Operator confirmation pending",
                ActiveAlertMessage = "The browser is ready for a remote viewer session before continuing the workflow.",
                Stages = new List<AccountWorkflowStageResponse>
                {
                    new() { StageCode = "login", StageName = "Login", State = "Completed", Message = "Login completed successfully.", OccurredAtUtc = DateTime.UtcNow.AddMinutes(-18) },
                    new() { StageCode = "slot_search", StageName = "Slot Search", State = "Completed", Message = "Matching slot found.", OccurredAtUtc = DateTime.UtcNow.AddMinutes(-7) },
                    new() { StageCode = "manual_review", StageName = "Manual Review", State = "ManualRequired", Message = "Browser left ready for operator takeover.", OccurredAtUtc = DateTime.UtcNow.AddMinutes(-2) }
                }
            }
        };

    private sealed class CommandPayload
    {
        public Guid? AccountId { get; set; }
    }

    private string BuildCommandResultMessage(NodeSummaryResponse node, AccountStageAlertDetailsResponse? account, string commandType)
    {
        return commandType switch
        {
            "OpenAssignedSession" when account is not null
                => $"Viewer ready. URL: http://{node.IpAddress}:6080/vnc.html?accountId={account.AccountId}",
            "FetchSessionLogs" when account is not null
                => $"Logs prepared for {account.Email}.",
            "StartBrowser" when account is not null
                => $"Start queued for {account.Email}.",
            "StopBrowser" when account is not null
                => $"Stop queued for {account.Email}.",
            _ => $"{commandType} executed for {node.Name}."
        };
    }
}
