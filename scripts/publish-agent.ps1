param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'linux-x64',
    [bool]$SelfContained = $true,
    [bool]$CreateBundle = $true
)

$ErrorActionPreference = 'Stop'

$repoRoot = [string](Resolve-Path (Join-Path $PSScriptRoot '..'))
$projectPath = Join-Path $repoRoot 'src\FleetManager.Agent\FleetManager.Agent.csproj'
$outputPath = Join-Path $repoRoot 'out\agent'
$bundleOutputDir = Join-Path $repoRoot 'out\bundles'
$bundleFileName = "fleetmanager-agent-bundle-$Runtime.zip"
$bundlePath = Join-Path $bundleOutputDir $bundleFileName
$bundleHashPath = "$bundlePath.sha256"
$bundleStagingPath = Join-Path $repoRoot 'out\bundle-staging'

if (Test-Path $outputPath) {
    Remove-Item -LiteralPath $outputPath -Recurse -Force
}

New-Item -ItemType Directory -Path $outputPath | Out-Null

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
Write-Host "FleetManager.Agent published to $outputPath"

if (-not $CreateBundle) {
    return
}

if (Test-Path $bundleStagingPath) {
    Remove-Item -LiteralPath $bundleStagingPath -Recurse -Force
}

if (Test-Path $bundlePath) {
    Remove-Item -LiteralPath $bundlePath -Force
}

if (Test-Path $bundleHashPath) {
    Remove-Item -LiteralPath $bundleHashPath -Force
}

New-Item -ItemType Directory -Path $bundleOutputDir -Force | Out-Null
New-Item -ItemType Directory -Path $bundleStagingPath -Force | Out-Null

$bundleAgentPath = Join-Path $bundleStagingPath 'agent'
$bundleDeployPath = Join-Path $bundleStagingPath 'deploy\linux'
New-Item -ItemType Directory -Path $bundleDeployPath -Force | Out-Null

Copy-Item -LiteralPath $outputPath -Destination $bundleAgentPath -Recurse
Copy-Item -Path (Join-Path $repoRoot 'deploy\linux\*') -Destination $bundleDeployPath -Recurse

$bundleMetadata = [ordered]@{
    generatedAtUtc = [DateTime]::UtcNow.ToString('O')
    runtime = $Runtime
    selfContained = $SelfContained
    agentDirectory = 'agent'
    installScript = 'deploy/linux/install-worker-ubuntu.sh'
}

$bundleMetadata | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $bundleStagingPath 'bundle-info.json')

Compress-Archive -Path (Join-Path $bundleStagingPath '*') -DestinationPath $bundlePath -CompressionLevel Optimal

$bundleHash = (Get-FileHash -LiteralPath $bundlePath -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath $bundleHashPath -Value "$bundleHash  $bundleFileName"

Remove-Item -LiteralPath $bundleStagingPath -Recurse -Force

Write-Host "GitHub bundle created at $bundlePath"
Write-Host "Bundle SHA256 written to $bundleHashPath"
