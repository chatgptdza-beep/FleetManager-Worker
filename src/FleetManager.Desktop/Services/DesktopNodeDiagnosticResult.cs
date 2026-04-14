namespace FleetManager.Desktop.Services;

public sealed class DesktopNodeDiagnosticResult
{
    public bool IsSshReachable { get; init; }
    public bool IsFleetManagerAgentActive { get; init; }
    public bool IsFleetManagerApiActive { get; init; }
    public bool IsDockerWorkerActive { get; init; }
    public string Detail { get; init; } = string.Empty;
}
