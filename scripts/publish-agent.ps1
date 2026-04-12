$ErrorActionPreference = 'Stop'

param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'linux-x64',
    [bool]$SelfContained = $true
)

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$projectPath = Join-Path $repoRoot 'src\FleetManager.Agent\FleetManager.Agent.csproj'
$outputPath = Join-Path $repoRoot 'out\agent'

if (Test-Path $outputPath) {
    Remove-Item -LiteralPath $outputPath -Recurse -Force
}

New-Item -ItemType Directory -Path $outputPath | Out-Null

$selfContainedArg = if ($SelfContained) { '--self-contained' } else { '--no-self-contained' }

dotnet publish $projectPath `
    -c $Configuration `
    -r $Runtime `
    $selfContainedArg `
    -o $outputPath

$hashLines = Get-ChildItem -LiteralPath $outputPath -File -Recurse |
    Sort-Object FullName |
    ForEach-Object {
        $relativePath = [System.IO.Path]::GetRelativePath($outputPath, $_.FullName).Replace('\', '/')
        $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash  $relativePath"
    }

Set-Content -LiteralPath (Join-Path $outputPath '.fleetmanager.sha256') -Value $hashLines

Write-Host "FleetManager.Agent published to $outputPath"
