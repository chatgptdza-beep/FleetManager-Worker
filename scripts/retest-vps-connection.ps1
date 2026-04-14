$ErrorActionPreference = 'Continue'

$desktopRuntimeStatePath = Join-Path $env:LOCALAPPDATA 'FleetManager\desktop.runtime.json'
$ip = if ($env:FLEETMANAGER_VPS_IP) { $env:FLEETMANAGER_VPS_IP } else { $null }
$apiBase = $null
$apiPassword = $env:FLEETMANAGER_API_PASSWORD
$rootPassword = $env:FLEETMANAGER_VPS_ROOT_PASSWORD

if ($env:FLEETMANAGER_API_BASE_URL) {
  $apiBase = $env:FLEETMANAGER_API_BASE_URL.TrimEnd('/')
}
elseif (Test-Path $desktopRuntimeStatePath) {
  try {
    $desktopRuntimeState = Get-Content $desktopRuntimeStatePath -Raw | ConvertFrom-Json
    if ($desktopRuntimeState -and $desktopRuntimeState.ApiBaseUrl) {
      $apiBase = $desktopRuntimeState.ApiBaseUrl.TrimEnd('/')
    }
  }
  catch {
    Write-Output ('RUNTIME_STATE_WARNING=' + $_.Exception.Message)
  }
}

Write-Output ('CHECK_IP=' + $ip)
Write-Output ('CHECK_API_BASE=' + $(if ($apiBase) { $apiBase } else { 'NOT_SET' }))

if ([string]::IsNullOrWhiteSpace($ip)) {
  Write-Output 'DECISION=MISSING_VPS_IP'
  Write-Output 'NEXT_STEP=Set FLEETMANAGER_VPS_IP for the newly added server and rerun the check.'
  exit 1
}

if ([string]::IsNullOrWhiteSpace($apiBase)) {
  Write-Output 'DECISION=MISSING_API_BASE'
  Write-Output ('NEXT_STEP=Pass the current API base URL from the app/session or launch Desktop once so it stores the current API in ' + $desktopRuntimeStatePath + '.')
  exit 1
}

# 1) Reset OpenSSH known_hosts entry to handle VPS reset/reimage
ssh-keygen -R $ip | Out-Null

# 2) API health
$apiOk = $false
try {
  $health = Invoke-WebRequest -Uri ($apiBase + '/health') -UseBasicParsing -TimeoutSec 8
  if ($health.StatusCode -eq 200) { $apiOk = $true }
  Write-Output ('API_HEALTH_STATUS=' + $health.StatusCode)
}
catch {
  Write-Output ('API_HEALTH_ERROR=' + $_.Exception.Message)
}

# 3) API auth + nodes check
$token = $null
$nodes = @()
if ($apiOk -and -not [string]::IsNullOrWhiteSpace($apiPassword)) {
  try {
    $authBody = @{ password = $apiPassword } | ConvertTo-Json -Compress
    $token = (Invoke-RestMethod -Uri ($apiBase + '/api/auth/token') -Method Post -ContentType 'application/json' -Body $authBody).token
    Write-Output 'API_AUTH=OK'

    $hdr = @{ Authorization = ('Bearer ' + $token) }
    $nodes = @(Invoke-RestMethod -Uri ($apiBase + '/api/nodes') -Headers $hdr)
    Write-Output ('NODES_COUNT=' + $nodes.Count)

    $match = @($nodes | Where-Object { $_.ipAddress -eq $ip })
    if ($match.Count -gt 0) {
      $n = $match[0]
      Write-Output ('NODE_MATCH_ID=' + $n.id)
      Write-Output ('NODE_MATCH_STATUS=' + $n.status)
      Write-Output ('NODE_MATCH_LAST_HEARTBEAT=' + $n.lastHeartbeatAtUtc)
    }
    else {
      Write-Output 'NODE_MATCH_STATUS=NOT_FOUND'
    }
  }
  catch {
    Write-Output ('API_AUTH_OR_NODES_ERROR=' + $_.Exception.Message)
  }
}
elseif ($apiOk) {
  Write-Output 'API_AUTH=SKIPPED_NO_PASSWORD'
}

# 4) SSH quick check (optional if password provided)
if (-not [string]::IsNullOrWhiteSpace($rootPassword)) {
  $plink = 'C:\Program Files\PuTTY\plink.exe'
  if (Test-Path $plink) {
    try {
      $out = & $plink -ssh -batch -pw $rootPassword ("root@{0}" -f $ip) "echo SSH_OK; systemctl is-active fleetmanager-api; systemctl is-active fleetmanager-agent" 2>&1
      $text = ($out | Out-String)
      Write-Output 'SSH_CHECK_OUTPUT_START'
      Write-Output $text.Trim()
      Write-Output 'SSH_CHECK_OUTPUT_END'
    }
    catch {
      Write-Output ('SSH_CHECK_ERROR=' + $_.Exception.Message)
    }
  }
  else {
    Write-Output 'SSH_CHECK_ERROR=PLINK_NOT_FOUND'
  }
}
else {
  Write-Output 'SSH_CHECK=SKIPPED_NO_PASSWORD'
}

# 5) Decision
if ($apiOk -and $token) {
  $nodeOnline = @($nodes | Where-Object { $_.ipAddress -eq $ip -and $_.status -eq 'Online' }).Count -gt 0
  if ($nodeOnline) {
    Write-Output 'DECISION=OK_REUSE_SAME_INFO'
    Write-Output 'NEXT_STEP=Continue using same VPS info; connectivity is healthy.'
  }
  else {
    Write-Output 'DECISION=REENTER_ONLY_IF_NEEDED'
    Write-Output 'NEXT_STEP=Node is not online or missing. Keep the same server record first; re-enter VPS info only if authentication or connectivity truly fails.'
  }
}
else {
  Write-Output 'DECISION=REENTER_ONLY_IF_NEEDED'
  Write-Output 'NEXT_STEP=Cannot reach or authenticate against the current API. Re-enter info only if the current credentials or endpoint are wrong.'
}
