namespace FleetManager.Desktop.Models;

public sealed class DesktopConfigSnapshot
{
    public string ApiBaseUrl { get; set; } = "http://localhost:5188/";
    public Guid? SelectedNodeId { get; set; }
    public string? SearchText { get; set; }
}
