using System.IO;
using System.Text.Json;

namespace FleetManager.Desktop.Models;

public static class DesktopRuntimeStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string StateFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FleetManager",
        "desktop.runtime.json");

    public static async Task<DesktopConfigSnapshot?> TryLoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(StateFilePath))
        {
            return null;
        }

        var json = await File.ReadAllTextAsync(StateFilePath, cancellationToken);
        return JsonSerializer.Deserialize<DesktopConfigSnapshot>(json);
    }

    public static async Task SaveAsync(DesktopConfigSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(StateFilePath)
            ?? throw new InvalidOperationException("State file directory could not be resolved.");
        Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(snapshot, JsonOptions);
        await File.WriteAllTextAsync(StateFilePath, json, cancellationToken);
    }
}