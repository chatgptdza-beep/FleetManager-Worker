namespace FleetManager.Desktop.Models;

public sealed class DesktopConfigSnapshot
{
    public string ApiBaseUrl { get; set; } = "http://82.223.9.98:5000/";
    public Guid? SelectedNodeId { get; set; }
    public string? SearchText { get; set; }
}
