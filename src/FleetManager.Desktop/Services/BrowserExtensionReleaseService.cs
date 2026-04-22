using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
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

    public async Task<PublishedBrowserExtensionRelease> PublishAsync(
        string sourcePath,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            throw new InvalidOperationException("Choose an extension source first. Use a folder path, manifest.json, or a .zip package.");
        }

        var token = DesktopEnvironment.ResolveGitHubAccessToken();
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException(
                "GitHub publishing requires FLEETMANAGER_GITHUB_TOKEN, GH_TOKEN, or GITHUB_TOKEN on this desktop.");
        }

        var repositoryUrl = DesktopEnvironment.ResolveRepositoryUrl();
        if (!GitHubReleaseAssetResolver.TryExtractRepositoryCoordinates(repositoryUrl, out var owner, out var repo))
        {
            throw new InvalidOperationException("The configured GitHub repository URL is invalid.");
        }

        var releaseTag = DesktopEnvironment.ResolveBrowserExtensionBundleReleaseTag();
        if (string.IsNullOrWhiteSpace(releaseTag))
        {
            throw new InvalidOperationException("No browser extension release tag is configured.");
        }

        progress?.Report("%04");
        progress?.Report("Validating local browser extension source...");

        var workRoot = Path.Combine(Path.GetTempPath(), "fleetmanager-browser-extension", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workRoot);

        try
        {
            var prepared = await PrepareBundleAsync(sourcePath, workRoot, progress, cancellationToken);
            progress?.Report("%34");
            progress?.Report("Connecting to GitHub Releases...");

            using var apiClient = CreateGitHubApiClient(token);
            var release = await GetOrCreateReleaseAsync(apiClient, owner, repo, releaseTag, prepared, cancellationToken);

            progress?.Report("%42");
            progress?.Report("Uploading browser extension bundle to GitHub...");
            release = await UploadAssetAsync(
                apiClient,
                owner,
                repo,
                release,
                prepared.BundlePath,
                FleetManagerReleaseDefaults.BrowserExtensionBundleFileName,
                "application/zip",
                cancellationToken);

            progress?.Report("%50");
            progress?.Report("Uploading browser extension checksum...");
            release = await UploadAssetAsync(
                apiClient,
                owner,
                repo,
                release,
                prepared.BundleSha256Path,
                FleetManagerReleaseDefaults.BrowserExtensionBundleSha256FileName,
                "text/plain; charset=utf-8",
                cancellationToken);

            if (!GitHubReleaseAssetResolver.TryBuildReleaseAssetUrl(
                    repositoryUrl,
                    releaseTag,
                    FleetManagerReleaseDefaults.BrowserExtensionBundleFileName,
                    out var bundleUrl)
                || !GitHubReleaseAssetResolver.TryBuildReleaseAssetUrl(
                    repositoryUrl,
                    releaseTag,
                    FleetManagerReleaseDefaults.BrowserExtensionBundleSha256FileName,
                    out var bundleSha256Url))
            {
                throw new InvalidOperationException("Failed to resolve browser extension release asset URLs.");
            }

            progress?.Report("%56");
            progress?.Report("Verifying published browser extension asset...");
            if (!await UrlExistsAsync(bundleUrl, cancellationToken))
            {
                throw new InvalidOperationException("GitHub published the release but the browser extension bundle is not reachable yet.");
            }

            progress?.Report("%58");
            progress?.Report($"Published {prepared.DisplayName} {prepared.Version} to GitHub Releases.");

            return new PublishedBrowserExtensionRelease(
                prepared.DisplayName,
                prepared.Version,
                bundleUrl,
                bundleSha256Url,
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

    public async Task<PublishedBrowserExtensionRelease?> TryGetPublishedReleaseAsync(CancellationToken cancellationToken = default)
    {
        var bundleUrl = DesktopEnvironment.ResolveBrowserExtensionBundleUrl();
        var bundleSha256Url = DesktopEnvironment.ResolveBrowserExtensionBundleSha256Url();
        if (string.IsNullOrWhiteSpace(bundleUrl) || string.IsNullOrWhiteSpace(bundleSha256Url))
        {
            return null;
        }

        if (!await UrlExistsAsync(bundleUrl, cancellationToken))
        {
            return null;
        }

        if (!await UrlExistsAsync(bundleSha256Url, cancellationToken))
        {
            return null;
        }

        return new PublishedBrowserExtensionRelease(
            "Fleet Browser Extension",
            "latest",
            bundleUrl,
            bundleSha256Url,
            DesktopEnvironment.ResolveBrowserExtensionBundleSha256() ?? string.Empty,
            DesktopEnvironment.ResolveManagedBrowserExtensionInstallPath());
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
        var bundleSha256Path = Path.Combine(workRoot, FleetManagerReleaseDefaults.BrowserExtensionBundleSha256FileName);
        await File.WriteAllTextAsync(
            bundleSha256Path,
            $"{bundleSha256}  {FleetManagerReleaseDefaults.BrowserExtensionBundleFileName}",
            new UTF8Encoding(false),
            cancellationToken);

        return new PreparedBundle(
            displayName ?? "Browser Extension",
            version ?? "unspecified",
            bundlePath,
            bundleSha256Path,
            bundleSha256);
    }

    private static ResolvedExtensionSource ResolveExtensionSource(string sourcePath, string workRoot)
    {
        var normalized = sourcePath.Trim().Trim('"');
        if (Directory.Exists(normalized))
        {
            return new ResolvedExtensionSource(ResolveExtensionRoot(normalized), null);
        }

        if (!File.Exists(normalized))
        {
            throw new InvalidOperationException($"Extension source path was not found: {normalized}");
        }

        if (string.Equals(Path.GetFileName(normalized), "manifest.json", StringComparison.OrdinalIgnoreCase))
        {
            var directory = Path.GetDirectoryName(normalized)
                ?? throw new InvalidOperationException("manifest.json path does not have a parent directory.");
            return new ResolvedExtensionSource(ResolveExtensionRoot(directory), null);
        }

        if (!string.Equals(Path.GetExtension(normalized), ".zip", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Unsupported extension source. Use a folder path, manifest.json, or a .zip package.");
        }

        var extractPath = Path.Combine(workRoot, "zip-source");
        ZipFile.ExtractToDirectory(normalized, extractPath);
        return new ResolvedExtensionSource(ResolveExtensionRoot(extractPath), extractPath);
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

    private static HttpClient CreateGitHubApiClient(string token)
    {
        var client = new HttpClient
        {
            BaseAddress = new Uri("https://api.github.com/")
        };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("FleetManagerDesktop", "1.0"));
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        return client;
    }

    private static async Task<GitHubReleaseResponse> GetOrCreateReleaseAsync(
        HttpClient client,
        string owner,
        string repo,
        string releaseTag,
        PreparedBundle prepared,
        CancellationToken cancellationToken)
    {
        using var lookupResponse = await client.GetAsync($"repos/{owner}/{repo}/releases/tags/{releaseTag}", cancellationToken);
        if (lookupResponse.StatusCode == HttpStatusCode.NotFound)
        {
            var createPayload = new
            {
                tag_name = releaseTag,
                name = $"FleetManager Browser Extension ({prepared.DisplayName})",
                body = $"Managed browser extension bundle for {prepared.DisplayName} {prepared.Version}.",
                draft = false,
                prerelease = false
            };

            using var createResponse = await client.PostAsync(
                $"repos/{owner}/{repo}/releases",
                JsonContent.Create(createPayload),
                cancellationToken);
            return await ReadGitHubResponseAsync<GitHubReleaseResponse>(createResponse, cancellationToken);
        }

        return await ReadGitHubResponseAsync<GitHubReleaseResponse>(lookupResponse, cancellationToken);
    }

    private static async Task<GitHubReleaseResponse> UploadAssetAsync(
        HttpClient apiClient,
        string owner,
        string repo,
        GitHubReleaseResponse release,
        string filePath,
        string assetName,
        string contentType,
        CancellationToken cancellationToken)
    {
        var existingAsset = release.Assets.FirstOrDefault(asset => string.Equals(asset.Name, assetName, StringComparison.OrdinalIgnoreCase));
        if (existingAsset is not null)
        {
            using var deleteResponse = await apiClient.DeleteAsync($"repos/{owner}/{repo}/releases/assets/{existingAsset.Id}", cancellationToken);
            if (deleteResponse.StatusCode != HttpStatusCode.NotFound)
            {
                deleteResponse.EnsureSuccessStatusCode();
            }
        }

        var uploadUrl = release.UploadUrl;
        var templateIndex = uploadUrl.IndexOf('{');
        if (templateIndex >= 0)
        {
            uploadUrl = uploadUrl[..templateIndex];
        }

        using var uploadClient = new HttpClient();
        uploadClient.DefaultRequestHeaders.Authorization = apiClient.DefaultRequestHeaders.Authorization;
        uploadClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        uploadClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("FleetManagerDesktop", "1.0"));
        uploadClient.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");

        await using var stream = File.OpenRead(filePath);
        using var content = new StreamContent(stream);
        content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);

        using var uploadResponse = await uploadClient.PostAsync(
            $"{uploadUrl}?name={Uri.EscapeDataString(assetName)}",
            content,
            cancellationToken);
        uploadResponse.EnsureSuccessStatusCode();

        using var refreshResponse = await apiClient.GetAsync($"repos/{owner}/{repo}/releases/{release.Id}", cancellationToken);
        return await ReadGitHubResponseAsync<GitHubReleaseResponse>(refreshResponse, cancellationToken);
    }

    private static async Task<T> ReadGitHubResponseAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(errorBody)
                ? $"GitHub API call failed. HTTP {(int)response.StatusCode}."
                : $"GitHub API call failed. HTTP {(int)response.StatusCode}: {errorBody}");
        }

        var payload = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        return payload ?? throw new InvalidOperationException("GitHub API returned an empty payload.");
    }

    private static async Task<bool> UrlExistsAsync(string url, CancellationToken cancellationToken)
    {
        using var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("FleetManagerDesktop", "1.0"));
        using var request = new HttpRequestMessage(HttpMethod.Head, url);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    private sealed record ResolvedExtensionSource(string ExtensionRootPath, string? CleanupDirectory);

    private sealed record PreparedBundle(
        string DisplayName,
        string Version,
        string BundlePath,
        string BundleSha256Path,
        string BundleSha256);

    private sealed class GitHubReleaseResponse
    {
        public long Id { get; set; }
        public string UploadUrl { get; set; } = string.Empty;
        public List<GitHubReleaseAssetResponse> Assets { get; set; } = new();
    }

    private sealed class GitHubReleaseAssetResponse
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }
}
