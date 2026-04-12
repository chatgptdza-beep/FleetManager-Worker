param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'linux-x64',
    [bool]$SelfContained = $true
)

$ErrorActionPreference = 'Stop'

$repoRoot = [string](Resolve-Path (Join-Path $PSScriptRoot '..'))
$projectPath = Join-Path $repoRoot 'src\FleetManager.Agent\FleetManager.Agent.csproj'
$outputPath = Join-Path $repoRoot 'out\agent'

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
