using FleetManager.Domain.Enums;

namespace FleetManager.Agent.Services;

public static class CommandAllowlist
{
    private static readonly HashSet<string> Allowed =
    [
        nameof(NodeCommandType.StartBrowser),
        nameof(NodeCommandType.StopBrowser),
        nameof(NodeCommandType.RestartBrowserWorker),
        nameof(NodeCommandType.OpenAssignedSession),
        nameof(NodeCommandType.CloseAssignedSession),
        nameof(NodeCommandType.BringManagedWindowToFront),
        nameof(NodeCommandType.CaptureScreenshot),
        nameof(NodeCommandType.FetchSessionLogs),
        nameof(NodeCommandType.RestartAgentService),
        nameof(NodeCommandType.ReloadApprovedConfig),
        nameof(NodeCommandType.UpdateAgentPackage),
        nameof(NodeCommandType.LoginWorkflow),
        nameof(NodeCommandType.StartAutomation),
        nameof(NodeCommandType.StopAutomation),
        nameof(NodeCommandType.PauseAutomation)
    ];

    public static bool IsAllowed(string commandName) => Allowed.Contains(commandName);
}
