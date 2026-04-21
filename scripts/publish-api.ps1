param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'linux-x64',
    [bool]$SelfContained = $true,
    [bool]$CreateBundle = $true
)

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$repoRoot = [string](Resolve-Path (Join-Path $PSScriptRoot '..'))
$projectPath = Join-Path $repoRoot 'src\FleetManager.Api\FleetManager.Api.csproj'
$outputPath = Join-Path $repoRoot 'out\api'
$bundleOutputDir = Join-Path $repoRoot 'out\bundles'
$bundleFileName = "fleetmanager-api-bundle-$Runtime.zip"
$bundlePath = Join-Path $bundleOutputDir $bundleFileName
$bundleHashPath = "$bundlePath.sha256"

if (Test-Path $outputPath) {
    Remove-Item -LiteralPath $outputPath -Recurse -Force
}

New-Item -ItemType Directory -Path $outputPath -Force | Out-Null

$selfContainedArg = if ($SelfContained) { '--self-contained' } else { '--no-self-contained' }

function Get-RelativeOutputPath {
    param(
        [string]$BasePath,
        [string]$FullPath
    )

    $normalizedBase = [System.IO.Path]::GetFullPath($BasePath).TrimEnd('\') + '\'
    $normalizedFull = [System.IO.Path]::GetFullPath($FullPath)

    if ($normalizedFull.StartsWith($normalizedBase, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $normalizedFull.Substring($normalizedBase.Length).Replace('\', '/')
    }

    return [System.IO.Path]::GetFileName($normalizedFull)
}

function New-ZipArchiveWithNormalizedPaths {
    param(
        [string]$SourcePath,
        [string]$DestinationPath
    )

    if (Test-Path $DestinationPath) {
        Remove-Item -LiteralPath $DestinationPath -Force
    }

    $archive = [System.IO.Compression.ZipFile]::Open($DestinationPath, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        Get-ChildItem -LiteralPath $SourcePath -File -Recurse |
            Sort-Object FullName |
            ForEach-Object {
                $relativePath = Get-RelativeOutputPath -BasePath $SourcePath -FullPath $_.FullName
                $entry = $archive.CreateEntry($relativePath.Replace('\', '/'), [System.IO.Compression.CompressionLevel]::Optimal)
                $entry.LastWriteTime = $_.LastWriteTimeUtc

                $input = [System.IO.File]::OpenRead($_.FullName)
                $output = $entry.Open()
                try {
                    $input.CopyTo($output)
                }
                finally {
                    $output.Dispose()
                    $input.Dispose()
                }
            }
    }
    finally {
        $archive.Dispose()
    }
}

dotnet publish $projectPath `
    -c $Configuration `
    -r $Runtime `
    $selfContainedArg `
    -o $outputPath

$hashLines = Get-ChildItem -LiteralPath $outputPath -File -Recurse |
    Sort-Object FullName |
    ForEach-Object {
        $relativePath = Get-RelativeOutputPath -BasePath $outputPath -FullPath $_.FullName
        $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash  $relativePath"
    }

Set-Content -LiteralPath (Join-Path $outputPath '.fleetmanager.sha256') -Value $hashLines
Write-Host "FleetManager.Api published to $outputPath"

if (-not $CreateBundle) {
    return
}

if (Test-Path $bundlePath) {
    Remove-Item -LiteralPath $bundlePath -Force
}

if (Test-Path $bundleHashPath) {
    Remove-Item -LiteralPath $bundleHashPath -Force
}

New-Item -ItemType Directory -Path $bundleOutputDir -Force | Out-Null

New-ZipArchiveWithNormalizedPaths -SourcePath $outputPath -DestinationPath $bundlePath

$bundleHash = (Get-FileHash -LiteralPath $bundlePath -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath $bundleHashPath -Value "$bundleHash  $bundleFileName"

Write-Host "GitHub bundle created at $bundlePath"
Write-Host "Bundle SHA256 written to $bundleHashPath"
