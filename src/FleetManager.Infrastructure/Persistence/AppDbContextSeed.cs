using FleetManager.Domain.Entities;
using FleetManager.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FleetManager.Infrastructure.Persistence;

public static class AppDbContextSeed
{
    public static async Task SeedAsync(AppDbContext dbContext, CancellationToken cancellationToken = default)
    {
        await dbContext.Database.EnsureCreatedAsync(cancellationToken);

        if (await dbContext.VpsNodes.AnyAsync(cancellationToken))
        {
            return;
        }

        var now = DateTime.UtcNow;

        var parisNodeId = Guid.Parse("3a5ff57d-e3d8-4d04-858e-fcef5b4997bf");
        var frankfurtNodeId = Guid.Parse("df8ec7ab-4bd8-43c8-bd6d-4b5ebf901437");
        var madridNodeId = Guid.Parse("70c8c145-a615-42eb-82bf-b93112f0fe12");
        var lagosNodeId = Guid.Parse("2f4a72af-4f7b-4c51-a3cb-b0ad6e3b3ecf");

        var alphaId = Guid.Parse("f58b7535-64fc-4569-bd57-c5eecc357f40");
        var bravoId = Guid.Parse("2bc0f75d-5df9-4fe9-96a0-67f8c4d8dc72");
        var charlieId = Guid.Parse("8eaa4352-f5a4-49de-9218-25a624cf96af");
        var deltaId = Guid.Parse("ab0cba77-63a0-4fef-bca0-738f08d2dc55");

        var parisNode = new VpsNode
        {
            Id = parisNodeId,
            Name = "VPS-PAR-01",
            IpAddress = "10.0.0.21",
            SshPort = 22,
            SshUsername = "deploy",
            AuthType = "SshKey",
            OsType = "Ubuntu 24.04",
            Region = "Paris",
            Status = NodeStatus.Online,
            LastHeartbeatAtUtc = now.AddSeconds(-18),
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
            AgentVersion = "1.0.0"
        };

        var frankfurtNode = new VpsNode
        {
            Id = frankfurtNodeId,
            Name = "VPS-FRA-03",
            IpAddress = "10.0.0.37",
            SshPort = 22,
            SshUsername = "deploy",
            AuthType = "SshKey",
            OsType = "Ubuntu 24.04",
            Region = "Frankfurt",
            Status = NodeStatus.Degraded,
            LastHeartbeatAtUtc = now.AddSeconds(-31),
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
            AgentVersion = "1.0.0"
        };

        var madridNode = new VpsNode
        {
            Id = madridNodeId,
            Name = "VPS-MAD-02",
            IpAddress = "10.0.0.52",
            SshPort = 22,
            SshUsername = "deploy",
            AuthType = "SshKey",
            OsType = "Ubuntu 24.04",
            Region = "Madrid",
            Status = NodeStatus.Online,
            LastHeartbeatAtUtc = now.AddSeconds(-12),
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
            AgentVersion = "1.0.0"
        };

        var lagosNode = new VpsNode
        {
            Id = lagosNodeId,
            Name = "VPS-LAG-04",
            IpAddress = "10.0.0.68",
            SshPort = 22,
            SshUsername = "deploy",
            AuthType = "SshKey",
            OsType = "Ubuntu 24.04",
            Region = "Lagos",
            Status = NodeStatus.Degraded,
            LastHeartbeatAtUtc = now.AddSeconds(-27),
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
            AgentVersion = "1.0.0"
        };

        parisNode.InstallJobs.Add(new AgentInstallJob
        {
            VpsNodeId = parisNodeId,
            JobStatus = InstallJobStatus.Succeeded,
            CurrentStep = "Completed",
            StartedAtUtc = now.AddHours(-4),
            EndedAtUtc = now.AddHours(-4).AddMinutes(2)
        });

        frankfurtNode.InstallJobs.Add(new AgentInstallJob
        {
            VpsNodeId = frankfurtNodeId,
            JobStatus = InstallJobStatus.Succeeded,
            CurrentStep = "Completed",
            StartedAtUtc = now.AddHours(-3),
            EndedAtUtc = now.AddHours(-3).AddMinutes(3)
        });

        madridNode.InstallJobs.Add(new AgentInstallJob
        {
            VpsNodeId = madridNodeId,
            JobStatus = InstallJobStatus.Succeeded,
            CurrentStep = "Completed",
            StartedAtUtc = now.AddHours(-2),
            EndedAtUtc = now.AddHours(-2).AddMinutes(2)
        });

        lagosNode.InstallJobs.Add(new AgentInstallJob
        {
            VpsNodeId = lagosNodeId,
            JobStatus = InstallJobStatus.Succeeded,
            CurrentStep = "Completed",
            StartedAtUtc = now.AddHours(-1),
            EndedAtUtc = now.AddHours(-1).AddMinutes(2)
        });

        var alpha = new Account
        {
            Id = alphaId,
            Email = "booking.alpha@example.com",
            Username = "booking.alpha",
            Status = AccountStatus.Running,
            VpsNodeId = parisNodeId,
            CurrentStageCode = "proxy_check",
            CurrentStageName = "Proxy Check",
            LastStageTransitionAtUtc = now.AddMinutes(-5)
        };
        alpha.WorkflowStages.Add(new AccountWorkflowStage
        {
            AccountId = alphaId,
            DisplayOrder = 1,
            StageCode = "login",
            StageName = "Login",
            State = WorkflowStageState.Completed,
            Message = "Credentials accepted.",
            OccurredAtUtc = now.AddMinutes(-12)
        });
        alpha.WorkflowStages.Add(new AccountWorkflowStage
        {
            AccountId = alphaId,
            DisplayOrder = 2,
            StageCode = "proxy_check",
            StageName = "Proxy Check",
            State = WorkflowStageState.Warning,
            Message = "Latency reached 4200 ms.",
            OccurredAtUtc = now.AddMinutes(-5)
        });
        alpha.WorkflowStages.Add(new AccountWorkflowStage
        {
            AccountId = alphaId,
            DisplayOrder = 3,
            StageCode = "slot_search",
            StageName = "Slot Search",
            State = WorkflowStageState.Running,
            Message = "Watching appointment inventory.",
            OccurredAtUtc = now.AddMinutes(-1)
        });
        alpha.WorkflowStages.Add(new AccountWorkflowStage
        {
            AccountId = alphaId,
            DisplayOrder = 4,
            StageCode = "payment",
            StageName = "Payment",
            State = WorkflowStageState.Pending,
            Message = "Not started yet."
        });
        alpha.Alerts.Add(new AccountAlert
        {
            AccountId = alphaId,
            StageCode = "proxy_check",
            StageName = "Proxy Check",
            Severity = AlertSeverity.Warning,
            Title = "Latency spike detected",
            Message = "Proxy validation exceeded the 4 second threshold. Keep the account under watch before auto-retry.",
            IsActive = true,
            CreatedAtUtc = now.AddMinutes(-5)
        });

        var bravo = new Account
        {
            Id = bravoId,
            Email = "booking.bravo@example.com",
            Username = "booking.bravo",
            Status = AccountStatus.Manual,
            VpsNodeId = frankfurtNodeId,
            CurrentStageCode = "captcha_solve",
            CurrentStageName = "Captcha Solve",
            LastStageTransitionAtUtc = now.AddMinutes(-4)
        };
        bravo.WorkflowStages.Add(new AccountWorkflowStage
        {
            AccountId = bravoId,
            DisplayOrder = 1,
            StageCode = "login",
            StageName = "Login",
            State = WorkflowStageState.Completed,
            Message = "Login completed successfully.",
            OccurredAtUtc = now.AddMinutes(-21)
        });
        bravo.WorkflowStages.Add(new AccountWorkflowStage
        {
            AccountId = bravoId,
            DisplayOrder = 2,
            StageCode = "profile_sync",
            StageName = "Profile Sync",
            State = WorkflowStageState.Completed,
            Message = "Profile data synced.",
            OccurredAtUtc = now.AddMinutes(-19)
        });
        bravo.WorkflowStages.Add(new AccountWorkflowStage
        {
            AccountId = bravoId,
            DisplayOrder = 3,
            StageCode = "captcha_solve",
            StageName = "Captcha Solve",
            State = WorkflowStageState.Failed,
            Message = "External solver timeout on three attempts.",
            OccurredAtUtc = now.AddMinutes(-4)
        });
        bravo.WorkflowStages.Add(new AccountWorkflowStage
        {
            AccountId = bravoId,
            DisplayOrder = 4,
            StageCode = "manual_review",
            StageName = "Manual Review",
            State = WorkflowStageState.ManualRequired,
            Message = "Operator acknowledgement pending.",
            OccurredAtUtc = now.AddMinutes(-3)
        });
        bravo.Alerts.Add(new AccountAlert
        {
            AccountId = bravoId,
            StageCode = "captcha_solve",
            StageName = "Captcha Solve",
            Severity = AlertSeverity.Critical,
            Title = "Stage failed after retries",
            Message = "Captcha provider timed out three times. Manual review is required before the workflow can continue.",
            IsActive = true,
            CreatedAtUtc = now.AddMinutes(-4)
        });

        var charlie = new Account
        {
            Id = charlieId,
            Email = "booking.charlie@example.com",
            Username = "booking.charlie",
            Status = AccountStatus.Stable,
            VpsNodeId = madridNodeId,
            CurrentStageCode = "slot_search",
            CurrentStageName = "Slot Search",
            LastStageTransitionAtUtc = now.AddMinutes(-1)
        };
        charlie.WorkflowStages.Add(new AccountWorkflowStage
        {
            AccountId = charlieId,
            DisplayOrder = 1,
            StageCode = "login",
            StageName = "Login",
            State = WorkflowStageState.Completed,
            Message = "Login completed.",
            OccurredAtUtc = now.AddMinutes(-16)
        });
        charlie.WorkflowStages.Add(new AccountWorkflowStage
        {
            AccountId = charlieId,
            DisplayOrder = 2,
            StageCode = "proxy_check",
            StageName = "Proxy Check",
            State = WorkflowStageState.Completed,
            Message = "Proxy healthy.",
            OccurredAtUtc = now.AddMinutes(-14)
        });
        charlie.WorkflowStages.Add(new AccountWorkflowStage
        {
            AccountId = charlieId,
            DisplayOrder = 3,
            StageCode = "slot_search",
            StageName = "Slot Search",
            State = WorkflowStageState.Running,
            Message = "No stage issues detected.",
            OccurredAtUtc = now.AddMinutes(-1)
        });
        charlie.WorkflowStages.Add(new AccountWorkflowStage
        {
            AccountId = charlieId,
            DisplayOrder = 4,
            StageCode = "payment",
            StageName = "Payment",
            State = WorkflowStageState.Pending,
            Message = "Waiting for slot match."
        });

        var delta = new Account
        {
            Id = deltaId,
            Email = "booking.delta@example.com",
            Username = "booking.delta",
            Status = AccountStatus.Paused,
            VpsNodeId = lagosNodeId,
            CurrentStageCode = "manual_review",
            CurrentStageName = "Manual Review",
            LastStageTransitionAtUtc = now.AddMinutes(-2)
        };
        delta.WorkflowStages.Add(new AccountWorkflowStage
        {
            AccountId = deltaId,
            DisplayOrder = 1,
            StageCode = "login",
            StageName = "Login",
            State = WorkflowStageState.Completed,
            Message = "Login completed successfully.",
            OccurredAtUtc = now.AddMinutes(-18)
        });
        delta.WorkflowStages.Add(new AccountWorkflowStage
        {
            AccountId = deltaId,
            DisplayOrder = 2,
            StageCode = "slot_search",
            StageName = "Slot Search",
            State = WorkflowStageState.Completed,
            Message = "Matching slot found.",
            OccurredAtUtc = now.AddMinutes(-7)
        });
        delta.WorkflowStages.Add(new AccountWorkflowStage
        {
            AccountId = deltaId,
            DisplayOrder = 3,
            StageCode = "manual_review",
            StageName = "Manual Review",
            State = WorkflowStageState.ManualRequired,
            Message = "Browser left ready for operator takeover.",
            OccurredAtUtc = now.AddMinutes(-2)
        });
        delta.Alerts.Add(new AccountAlert
        {
            AccountId = deltaId,
            StageCode = "manual_review",
            StageName = "Manual Review",
            Severity = AlertSeverity.ManualRequired,
            Title = "Operator confirmation pending",
            Message = "The browser is ready for a remote viewer session before continuing the workflow.",
            IsActive = true,
            CreatedAtUtc = now.AddMinutes(-2)
        });

        parisNode.Accounts.Add(alpha);
        frankfurtNode.Accounts.Add(bravo);
        madridNode.Accounts.Add(charlie);
        lagosNode.Accounts.Add(delta);

        await dbContext.VpsNodes.AddRangeAsync(new[] { parisNode, frankfurtNode, madridNode, lagosNode }, cancellationToken);
        await dbContext.NodeCapabilities.AddRangeAsync(new[]
        {
            new NodeCapability
            {
                VpsNodeId = parisNodeId,
                CanStartBrowser = true,
                CanStopBrowser = true,
                CanRestartBrowser = true,
                CanCaptureScreenshot = true,
                CanFetchLogs = true,
                CanUpdateAgent = true
            },
            new NodeCapability
            {
                VpsNodeId = frankfurtNodeId,
                CanStartBrowser = true,
                CanStopBrowser = true,
                CanRestartBrowser = true,
                CanCaptureScreenshot = true,
                CanFetchLogs = true,
                CanUpdateAgent = true
            },
            new NodeCapability
            {
                VpsNodeId = madridNodeId,
                CanStartBrowser = true,
                CanStopBrowser = true,
                CanRestartBrowser = true,
                CanCaptureScreenshot = true,
                CanFetchLogs = true,
                CanUpdateAgent = true
            },
            new NodeCapability
            {
                VpsNodeId = lagosNodeId,
                CanStartBrowser = true,
                CanStopBrowser = true,
                CanRestartBrowser = true,
                CanCaptureScreenshot = true,
                CanFetchLogs = true,
                CanUpdateAgent = true
            }
        }, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
