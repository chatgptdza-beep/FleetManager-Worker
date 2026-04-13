namespace FleetManager.Contracts;

/// <summary>
/// Single source of truth for the set of commands the agent is allowed to execute.
/// Referenced by both the API (dispatch validation) and the Agent (execution guard).
/// </summary>
public static class CommandAllowlist
{
    private static readonly HashSet<string> Allowed = new(StringComparer.Ordinal)
    {
        "StartBrowser",
        "StopBrowser",
        "RestartBrowserWorker",
        "OpenAssignedSession",
        "CloseAssignedSession",
        "BringManagedWindowToFront",
        "CaptureScreenshot",
        "FetchSessionLogs",
        "RestartAgentService",
        "ReloadApprovedConfig",
        "UpdateAgentPackage",
        "LoginWorkflow",
        "StartAutomation",
        "StopAutomation",
        "PauseAutomation"
    };

    public static bool IsAllowed(string commandName) => Allowed.Contains(commandName);

    public static IReadOnlySet<string> AllowedCommands => Allowed;
}
