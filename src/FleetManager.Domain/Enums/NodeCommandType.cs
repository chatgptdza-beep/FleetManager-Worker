namespace FleetManager.Domain.Enums;

public enum NodeCommandType
{
    None = 0,
    StartBrowser = 1,
    StopBrowser = 2,
    RestartBrowserWorker = 3,
    OpenAssignedSession = 4,
    CloseAssignedSession = 5,
    BringManagedWindowToFront = 6,
    CaptureScreenshot = 7,
    FetchSessionLogs = 8,
    RestartAgentService = 9,
    ReloadApprovedConfig = 10,
    UpdateAgentPackage = 11,
    UpdateNodeStack = 12,
    LoginWorkflow = 13,
    StartAutomation = 14,
    StopAutomation = 15,
    PauseAutomation = 16,
    RefreshProxyPool = 17,
    TakeoverComplete = 18
}
