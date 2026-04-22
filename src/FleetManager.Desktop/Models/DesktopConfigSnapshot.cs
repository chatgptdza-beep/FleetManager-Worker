namespace FleetManager.Desktop.Models;

public sealed class DesktopConfigSnapshot
{
    public string ApiBaseUrl { get; set; } = string.Empty;
    public Guid? SelectedNodeId { get; set; }
    public string? SearchText { get; set; }
    public string? BrowserExtensionSourcePath { get; set; }
}
