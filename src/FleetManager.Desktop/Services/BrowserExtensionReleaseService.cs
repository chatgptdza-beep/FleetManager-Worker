using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FleetManager.Contracts.Configuration;
using FleetManager.Desktop.Models;

namespace FleetManager.Desktop.Services;

public sealed class BrowserExtensionReleaseService : IBrowserExtensionReleaseService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public async Task<PreparedBrowserExtensionPackage> PrepareAsync(
        string sourcePath,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            throw new InvalidOperationException("Choose an extension source first. Use a folder path, manifest.json, or a .zip package.");
        }

        progress?.Report("%04");
        progress?.Report("Validating local browser extension source...");

        var workRoot = Path.Combine(Path.GetTempPath(), "fleetmanager-browser-extension", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workRoot);

        try
        {
            var prepared = await PrepareBundleAsync(sourcePath, workRoot, progress, cancellationToken);
            progress?.Report("%36");
            progress?.Report("Encoding the browser extension bundle for direct VPS upload...");

            var bundleBytes = await File.ReadAllBytesAsync(prepared.BundlePath, cancellationToken);
            var bundleBase64 = Convert.ToBase64String(bundleBytes);

            progress?.Report("%40");
            progress?.Report($"Prepared {prepared.DisplayName} {prepared.Version} for direct VPS rollout.");

            return new PreparedBrowserExtensionPackage(
                prepared.DisplayName,
                prepared.Version,
                prepared.SourcePath,
                bundleBase64,
                prepared.BundleSha256,
                DesktopEnvironment.ResolveManagedBrowserExtensionInstallPath());
        }
        finally
        {
            try
            {
                if (Directory.Exists(workRoot))
                {
                    Directory.Delete(workRoot, recursive: true);
                }
            }
            catch
            {
                // Best-effort temp cleanup.
            }
        }
    }

    public async Task<PreparedBrowserExtensionPackage?> TryPrepareFromSourceAsync(
        string? sourcePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return null;
        }

        var normalized = sourcePath.Trim().Trim('"');
        if (!Directory.Exists(normalized) && !File.Exists(normalized))
        {
            return null;
        }

        return await PrepareAsync(normalized, progress: null, cancellationToken);
    }

    private static async Task<PreparedBundle> PrepareBundleAsync(
        string sourcePath,
        string workRoot,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report("%08");
        progress?.Report("Resolving manifest and extension root...");

        var resolvedSource = ResolveExtensionSource(sourcePath, workRoot);
        var manifestPath = Path.Combine(resolvedSource.ExtensionRootPath, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            throw new InvalidOperationException("The resolved extension root does not contain manifest.json.");
        }

        progress?.Report("%14");
        progress?.Report("Reading extension manifest metadata...");

        await using var manifestStream = File.OpenRead(manifestPath);
        using var manifest = await JsonDocument.ParseAsync(manifestStream, cancellationToken: cancellationToken);
        var displayName = manifest.RootElement.TryGetProperty("name", out var nameElement) && nameElement.ValueKind == JsonValueKind.String
            ? nameElement.GetString()?.Trim()
            : null;
        var version = manifest.RootElement.TryGetProperty("version", out var versionElement) && versionElement.ValueKind == JsonValueKind.String
            ? versionElement.GetString()?.Trim()
            : null;

        displayName = string.IsNullOrWhiteSpace(displayName)
            ? Path.GetFileName(resolvedSource.ExtensionRootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            : displayName;
        version = string.IsNullOrWhiteSpace(version) ? "unspecified" : version;

        progress?.Report("%22");
        progress?.Report("Building normalized browser extension bundle...");

        var stagingPath = Path.Combine(workRoot, "bundle-staging");
        var extensionStagingPath = Path.Combine(stagingPath, "extension");
        Directory.CreateDirectory(extensionStagingPath);
        CopyDirectory(resolvedSource.ExtensionRootPath, extensionStagingPath);

        var metadataPath = Path.Combine(stagingPath, "extension-info.json");
        var metadata = new
        {
            displayName,
            version,
            installPath = DesktopEnvironment.ResolveManagedBrowserExtensionInstallPath(),
            sourcePath = resolvedSource.SourcePath,
            generatedAtUtc = DateTime.UtcNow.ToString("O")
        };
        await File.WriteAllTextAsync(
            metadataPath,
            JsonSerializer.Serialize(metadata, JsonOptions),
            new UTF8Encoding(false),
            cancellationToken);

        var bundlePath = Path.Combine(workRoot, FleetManagerReleaseDefaults.BrowserExtensionBundleFileName);
        CreateNormalizedZip(stagingPath, bundlePath);

        progress?.Report("%30");
        progress?.Report("Computing browser extension bundle checksum...");

        var bundleSha256 = ComputeSha256(bundlePath);

        return new PreparedBundle(
            displayName ?? "Browser Extension",
            version ?? "unspecified",
            resolvedSource.SourcePath,
            bundlePath,
            bundleSha256);
    }

    private static ResolvedExtensionSource ResolveExtensionSource(string sourcePath, string workRoot)
    {
        var normalized = Path.GetFullPath(sourcePath.Trim().Trim('"'));
        if (Directory.Exists(normalized))
        {
            return new ResolvedExtensionSource(ResolveExtensionRoot(normalized), normalized);
        }

        if (!File.Exists(normalized))
        {
            throw new InvalidOperationException($"Extension source path was not found: {normalized}");
        }

        if (string.Equals(Path.GetFileName(normalized), "manifest.json", StringComparison.OrdinalIgnoreCase))
        {
            var directory = Path.GetDirectoryName(normalized)
                ?? throw new InvalidOperationException("manifest.json path does not have a parent directory.");
            return new ResolvedExtensionSource(ResolveExtensionRoot(directory), normalized);
        }

        if (!string.Equals(Path.GetExtension(normalized), ".zip", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Unsupported extension source. Use a folder path, manifest.json, or a .zip package.");
        }

        var extractPath = Path.Combine(workRoot, "zip-source");
        ZipFile.ExtractToDirectory(normalized, extractPath);
        return new ResolvedExtensionSource(ResolveExtensionRoot(extractPath), normalized);
    }

    private static string ResolveExtensionRoot(string candidatePath)
    {
        var rootManifest = Path.Combine(candidatePath, "manifest.json");
        if (File.Exists(rootManifest))
        {
            return candidatePath;
        }

        var manifests = Directory.EnumerateFiles(candidatePath, "manifest.json", SearchOption.AllDirectories)
            .Select(path => new
            {
                ManifestPath = path,
                Depth = GetDirectoryDepth(candidatePath, Path.GetDirectoryName(path) ?? candidatePath)
            })
            .OrderBy(entry => entry.Depth)
            .ThenBy(entry => entry.ManifestPath.Length)
            .ToList();

        if (manifests.Count == 0)
        {
            throw new InvalidOperationException($"No manifest.json was found under {candidatePath}.");
        }

        return Path.GetDirectoryName(manifests[0].ManifestPath)
            ?? throw new InvalidOperationException("Resolved manifest path does not have a parent directory.");
    }

    private static int GetDirectoryDepth(string baseDirectory, string candidateDirectory)
    {
        var relative = Path.GetRelativePath(baseDirectory, candidateDirectory);
        if (string.Equals(relative, ".", StringComparison.Ordinal))
        {
            return 0;
        }

        return relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries).Length;
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);

        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDirectory, directory);
            Directory.CreateDirectory(Path.Combine(destinationDirectory, relative));
        }

        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDirectory, file);
            var targetPath = Path.Combine(destinationDirectory, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.Copy(file, targetPath, overwrite: true);
        }
    }

    private static void CreateNormalizedZip(string sourceDirectory, string destinationPath)
    {
        if (File.Exists(destinationPath))
        {
            File.Delete(destinationPath);
        }

        using var archive = ZipFile.Open(destinationPath, ZipArchiveMode.Create);
        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var relative = Path.GetRelativePath(sourceDirectory, file).Replace('\\', '/');
            var entry = archive.CreateEntry(relative, CompressionLevel.Optimal);
            entry.LastWriteTime = File.GetLastWriteTimeUtc(file);

            using var input = File.OpenRead(file);
            using var output = entry.Open();
            input.CopyTo(output);
        }
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha256 = SHA256.Create();
        return Convert.ToHexString(sha256.ComputeHash(stream)).ToLowerInvariant();
    }

    private sealed record ResolvedExtensionSource(string ExtensionRootPath, string SourcePath);

    private sealed record PreparedBundle(
        string DisplayName,
        string Version,
        string SourcePath,
        string BundlePath,
        string BundleSha256);
}
