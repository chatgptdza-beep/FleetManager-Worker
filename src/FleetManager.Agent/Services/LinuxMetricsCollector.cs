using System.Runtime.InteropServices;

namespace FleetManager.Agent.Services;

/// <summary>
/// Collects real system metrics from /proc on Linux.
/// Falls back to zeros on non-Linux platforms (dev/Windows).
/// </summary>
public sealed class LinuxMetricsCollector
{
    private long _prevIdleTime;
    private long _prevTotalTime;
    private bool _firstCpuRead = true;

    /// <summary>
    /// CPU usage percentage (0–100), read from /proc/stat on Linux.
    /// </summary>
    public double GetCpuPercent()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return 0;

        try
        {
            var line = File.ReadLines("/proc/stat").FirstOrDefault(l => l.StartsWith("cpu "));
            if (line is null) return 0;

            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            // cpu  user nice system idle iowait irq softirq steal
            long user    = long.Parse(parts[1]);
            long nice    = long.Parse(parts[2]);
            long system  = long.Parse(parts[3]);
            long idle    = long.Parse(parts[4]);
            long iowait  = long.Parse(parts[5]);
            long irq     = long.Parse(parts[6]);
            long softirq = long.Parse(parts[7]);

            long idleTime  = idle + iowait;
            long totalTime = user + nice + system + idle + iowait + irq + softirq;

            if (_firstCpuRead)
            {
                _prevIdleTime  = idleTime;
                _prevTotalTime = totalTime;
                _firstCpuRead  = false;
                return 0;
            }

            long deltaIdle  = idleTime  - _prevIdleTime;
            long deltaTotal = totalTime - _prevTotalTime;

            _prevIdleTime  = idleTime;
            _prevTotalTime = totalTime;

            if (deltaTotal == 0) return 0;
            return Math.Round((1.0 - (double)deltaIdle / deltaTotal) * 100.0, 1);
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// RAM usage percentage (0–100), read from /proc/meminfo on Linux.
    /// </summary>
    public (double Percent, double UsedGb) GetRamInfo()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return (0, 0);

        try
        {
            var lines = File.ReadAllLines("/proc/meminfo");
            long totalKb     = ParseMemInfoLine(lines, "MemTotal:");
            long availableKb = ParseMemInfoLine(lines, "MemAvailable:");

            if (totalKb == 0) return (0, 0);

            long usedKb    = totalKb - availableKb;
            double percent = Math.Round((double)usedKb / totalKb * 100.0, 1);
            double usedGb  = Math.Round(usedKb / 1024.0 / 1024.0, 2);
            return (percent, usedGb);
        }
        catch
        {
            return (0, 0);
        }
    }

    /// <summary>
    /// Disk usage percentage and used GB for /var/lib/fleetmanager (or /).
    /// </summary>
    public (double Percent, double UsedGb) GetDiskInfo()
    {
        try
        {
            var path = Directory.Exists("/var/lib/fleetmanager")
                ? "/var/lib/fleetmanager"
                : "/";

            var drive = new DriveInfo(path);
            if (drive.TotalSize == 0) return (0, 0);

            long usedBytes = drive.TotalSize - drive.AvailableFreeSpace;
            double percent = Math.Round((double)usedBytes / drive.TotalSize * 100.0, 1);
            double usedGb  = Math.Round(usedBytes / 1024.0 / 1024.0 / 1024.0, 2);
            return (percent, usedGb);
        }
        catch
        {
            return (0, 0);
        }
    }

    /// <summary>
    /// Counts running fleet-browser Docker containers via /proc process names.
    /// Uses Docker CLI as a fallback if available.
    /// </summary>
    public static int GetActiveSessionCount()
    {
        try
        {
            // Count process entries whose cmdline contains "fleet-browser"
            int count = 0;
            foreach (var dir in Directory.EnumerateDirectories("/proc"))
            {
                if (!int.TryParse(Path.GetFileName(dir), out _)) continue;
                var cmdlineFile = Path.Combine(dir, "cmdline");
                if (!File.Exists(cmdlineFile)) continue;
                var cmdline = File.ReadAllText(cmdlineFile).Replace('\0', ' ');
                if (cmdline.Contains("fleet-browser", StringComparison.OrdinalIgnoreCase))
                    count++;
            }
            return count;
        }
        catch
        {
            return 0;
        }
    }

    private static long ParseMemInfoLine(string[] lines, string key)
    {
        foreach (var line in lines)
        {
            if (!line.StartsWith(key)) continue;
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length >= 2 && long.TryParse(parts[1], out long v) ? v : 0;
        }
        return 0;
    }
}
