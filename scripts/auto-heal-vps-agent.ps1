$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

$projectRoot = [string](Resolve-Path (Join-Path $PSScriptRoot '..'))
$desktopRuntimeStatePath = Join-Path $env:LOCALAPPDATA 'FleetManager\desktop.runtime.json'
$ip = if ($env:FLEETMANAGER_VPS_IP) { $env:FLEETMANAGER_VPS_IP } else { $null }
$rootPassword = if ($env:FLEETMANAGER_VPS_ROOT_PASSWORD) { $env:FLEETMANAGER_VPS_ROOT_PASSWORD } else { $null }
$apiBase = $null
$apiPassword = if ($env:FLEETMANAGER_API_PASSWORD) { $env:FLEETMANAGER_API_PASSWORD } else { $null }
$agentApiKey = if ($env:FLEETMANAGER_AGENT_API_KEY) { $env:FLEETMANAGER_AGENT_API_KEY } else { $null }
$hostKey = if ($env:FLEETMANAGER_SSH_HOSTKEY) { $env:FLEETMANAGER_SSH_HOSTKEY } else { 'ssh-ed25519 255 SHA256:nHMjdmJ1cHzkLLY/8LQwFdhgA7nAoxg16GgBmKBXbI8' }

$browserExtensions = @()
if ($env:FLEETMANAGER_BROWSER_EXTENSIONS) {
  $browserExtensions = @($env:FLEETMANAGER_BROWSER_EXTENSIONS.Split(',') | ForEach-Object { $_.Trim() } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
}

if ($browserExtensions.Count -eq 0) {
  $browserExtensions = @('/opt/fleetmanager-agent/extensions/quickreserve-loader')
}

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

$plink = 'C:\Program Files\PuTTY\plink.exe'
$pscp = 'C:\Program Files\PuTTY\pscp.exe'
if (-not (Test-Path $plink)) { throw "plink not found: $plink" }
if (-not (Test-Path $pscp)) { throw "pscp not found: $pscp" }

function Invoke-Remote {
  param([string]$Command)
  & $plink -ssh -batch -hostkey $hostKey -pw $rootPassword ("root@{0}" -f $ip) $Command 2>&1
}

function Write-AgentConfig {
  param([string]$ResolvedNodeId)

  $configJson = @{
    Agent = @{
      NodeId = $ResolvedNodeId
      BackendBaseUrl = $apiBase
      HeartbeatIntervalSeconds = 10
      CommandPollIntervalSeconds = 2
      CommandTimeoutMinutes = 5
      AgentVersion = '0.1.0'
      CommandScriptsPath = '/opt/fleetmanager-agent/commands'
      ApiKey = $agentApiKey
      NodeIpAddress = $ip
      EnableDockerMonitor = $false
      BrowserExtensions = $browserExtensions
    }
  } | ConvertTo-Json -Depth 4

  $configBase64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($configJson))
  $remoteConfig = "printf '%s' '$configBase64' | base64 -d > /opt/fleetmanager-agent/appsettings.json; chown fleetmgr:fleetmgr /opt/fleetmanager-agent/appsettings.json"
  Invoke-Remote $remoteConfig | Out-Null
}

function Set-RemoteBrowserExtensionsOverride {
  param([string[]]$Extensions)

  $normalized = @($Extensions | ForEach-Object { $_.Trim() } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
  if ($normalized.Count -eq 0) {
    Write-Output 'BROWSER_EXTENSIONS_OVERRIDE=SKIPPED_EMPTY'
    return
  }

  $extensionsCsv = $normalized -join ','
  $overrideContent = "[Service]`nEnvironment=FM_BROWSER_EXTENSIONS=$extensionsCsv`n"
  $overrideBase64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($overrideContent))
  $remoteOverrideCmd = "mkdir -p /etc/systemd/system/fleetmanager-agent.service.d; printf '%s' '$overrideBase64' | base64 -d > /etc/systemd/system/fleetmanager-agent.service.d/10-browser-extensions.conf"
  Invoke-Remote $remoteOverrideCmd | Out-Null
  Write-Output ('BROWSER_EXTENSIONS_OVERRIDE=' + $extensionsCsv)
}

function Get-RemoteConfiguredNodeId {
  try {
    $remoteConfigJson = (Invoke-Remote 'cat /opt/fleetmanager-agent/appsettings.json 2>/dev/null || true') | Out-String
    if ([string]::IsNullOrWhiteSpace($remoteConfigJson)) {
      return ''
    }

    $remoteConfig = $remoteConfigJson | ConvertFrom-Json
    if ($remoteConfig -and $remoteConfig.Agent -and $remoteConfig.Agent.NodeId) {
      return $remoteConfig.Agent.NodeId.Trim()
    }
  }
  catch {
    Write-Output ('REMOTE_CONFIG_PARSE_WARNING=' + $_.Exception.Message)
  }

  return ''
}

function Get-RemoteConfigObject {
  try {
    $remoteConfigJson = (Invoke-Remote 'cat /opt/fleetmanager-agent/appsettings.json 2>/dev/null || true') | Out-String
    if ([string]::IsNullOrWhiteSpace($remoteConfigJson)) {
      return $null
    }

    return $remoteConfigJson | ConvertFrom-Json
  }
  catch {
    Write-Output ('REMOTE_CONFIG_PARSE_WARNING=' + $_.Exception.Message)
    return $null
  }
}

Write-Output ('TARGET_IP=' + $ip)
Write-Output ('API_BASE=' + $(if ($apiBase) { $apiBase } else { 'NOT_SET' }))

if ([string]::IsNullOrWhiteSpace($ip)) {
  Write-Output 'RESULT=MISSING_VPS_IP'
  Write-Output 'ACTION=Set FLEETMANAGER_VPS_IP for the new server before running auto-heal.'
  exit 1
}

if ([string]::IsNullOrWhiteSpace($rootPassword)) {
  Write-Output 'RESULT=MISSING_ROOT_PASSWORD'
  Write-Output 'ACTION=Set FLEETMANAGER_VPS_ROOT_PASSWORD for the new server before running auto-heal.'
  exit 1
}

if ([string]::IsNullOrWhiteSpace($apiBase)) {
  Write-Output 'RESULT=MISSING_API_BASE'
  Write-Output ('ACTION=Pass the current API base URL from the app/session or launch Desktop once so it stores the current API in ' + $desktopRuntimeStatePath + '.')
  exit 1
}

if ([string]::IsNullOrWhiteSpace($apiPassword)) {
  Write-Output 'RESULT=MISSING_API_PASSWORD'
  Write-Output 'ACTION=Set FLEETMANAGER_API_PASSWORD before running auto-heal.'
  exit 1
}

if ([string]::IsNullOrWhiteSpace($agentApiKey)) {
  Write-Output 'RESULT=MISSING_AGENT_API_KEY'
  Write-Output 'ACTION=Set FLEETMANAGER_AGENT_API_KEY before running auto-heal.'
  exit 1
}

# Ensure OpenSSH known_hosts does not block after VPS reset/reimage.
ssh-keygen -R $ip | Out-Null

$sshProbe = (Invoke-Remote "echo SSH_OK; uname -a") | Out-String
if ($sshProbe -notmatch 'SSH_OK') {
  Write-Output 'RESULT=SSH_FAILED'
  Write-Output 'ACTION=Please re-enter VPS SSH info only if credentials changed or SSH auth fails.'
  Write-Output $sshProbe.Trim()
  exit 1
}

Write-Output 'SSH_STATUS=OK'

$apiProbe = (Invoke-Remote "if systemctl list-unit-files | grep -q '^fleetmanager-api.service'; then echo API_UNIT_PRESENT=1; systemctl is-active fleetmanager-api 2>/dev/null | sed 's/^/API_SERVICE=/' || echo API_SERVICE=inactive; else echo API_UNIT_PRESENT=0; fi") | Out-String
Write-Output $apiProbe.Trim()
if ($apiProbe -match 'API_UNIT_PRESENT=1' -and $apiProbe -notmatch 'API_SERVICE=active') {
  Write-Output 'API_ACTION=restart_before_discovery'
  try {
    $apiRestart = (Invoke-Remote "systemctl daemon-reload; systemctl restart fleetmanager-api 2>/dev/null || true; sleep 2; systemctl is-active fleetmanager-api 2>/dev/null | sed 's/^/API_SERVICE=/' || echo API_SERVICE=inactive") | Out-String
    Write-Output $apiRestart.Trim()
  }
  catch {
    Write-Output ('API_RESTART_WARNING=' + $_.Exception.Message)
  }
}

$inspect = (Invoke-Remote "if systemctl list-unit-files | grep -q '^fleetmanager-agent.service'; then echo UNIT_PRESENT=1; else echo UNIT_PRESENT=0; fi; if [ -x /opt/fleetmanager-agent/FleetManager.Agent ] || [ -f /opt/fleetmanager-agent/FleetManager.Agent.dll ]; then echo BIN_PRESENT=1; else echo BIN_PRESENT=0; fi; systemctl is-active fleetmanager-agent 2>/dev/null | sed 's/^/SERVICE_ACTIVE=/' || echo SERVICE_ACTIVE=missing") | Out-String
Write-Output $inspect.Trim()

$needsReinstall = $false
if ($inspect -notmatch 'UNIT_PRESENT=1') { $needsReinstall = $true }
if ($inspect -notmatch 'BIN_PRESENT=1') { $needsReinstall = $true }

if (-not $needsReinstall -and $inspect -notmatch 'SERVICE_ACTIVE=active') {
  Write-Output 'SERVICE_ACTION=restart'
  Invoke-Remote "systemctl restart fleetmanager-agent; sleep 2; systemctl is-active fleetmanager-agent | sed 's/^/SERVICE_ACTIVE=/'" | Out-Null
  $afterRestart = (Invoke-Remote "systemctl is-active fleetmanager-agent | sed 's/^/SERVICE_ACTIVE=/'") | Out-String
  Write-Output $afterRestart.Trim()
  if ($afterRestart -notmatch 'SERVICE_ACTIVE=active') {
    $needsReinstall = $true
  }
}

$nodeId = $null
$token = $null
try {
  $authBody = @{ password = $apiPassword } | ConvertTo-Json -Compress
  $token = (Invoke-RestMethod -Uri ($apiBase + '/api/auth/token') -Method Post -ContentType 'application/json' -Body $authBody -TimeoutSec 8).token
  $hdr = @{ Authorization = ('Bearer ' + $token) }
  $nodes = @(Invoke-RestMethod -Uri ($apiBase + '/api/nodes') -Headers $hdr -TimeoutSec 8)
  $matched = @($nodes | Where-Object { $_.ipAddress -eq $ip } | Select-Object -First 1)
  if ($matched) {
    $nodeId = $matched.id
    Write-Output ('MATCHED_NODE_ID=' + $nodeId)
  }
  else {
    Write-Output 'MATCHED_NODE_ID=NOT_FOUND'
  }
}
catch {
  Write-Output ('API_DISCOVERY_ERROR=' + $_.Exception.Message)
}

if ($needsReinstall) {
  Write-Output 'AGENT_ACTION=reinstall'

  $publishDir = Join-Path $projectRoot 'output\agent-autoheal-publish'
  if (Test-Path $publishDir) {
    Remove-Item -Recurse -Force $publishDir
  }

  Push-Location $projectRoot
  try {
    Write-Output 'STEP=dotnet_publish_agent'
    dotnet publish src/FleetManager.Agent/FleetManager.Agent.csproj -c Release -o $publishDir
  }
  finally {
    Pop-Location
  }

  Write-Output 'STEP=remote_prepare_tmp_dirs'
  Invoke-Remote "mkdir -p /tmp/fleetmanager-agent-autoheal /tmp/fleetmanager-linux-deploy"

  Write-Output 'STEP=upload_agent_publish'
  & $pscp -batch -hostkey $hostKey -pw $rootPassword -r (Join-Path $publishDir '*') ("root@{0}:/tmp/fleetmanager-agent-autoheal/" -f $ip)
  if ($LASTEXITCODE -ne 0) { throw "PSCP upload agent publish failed with code $LASTEXITCODE" }

  Write-Output 'STEP=upload_linux_deploy_scripts'
  & $pscp -batch -hostkey $hostKey -pw $rootPassword -r (Join-Path $projectRoot 'deploy\\linux\\*') ("root@{0}:/tmp/fleetmanager-linux-deploy/" -f $ip)
  if ($LASTEXITCODE -ne 0) { throw "PSCP upload linux deploy scripts failed with code $LASTEXITCODE" }

  Write-Output 'STEP=run_remote_install_worker'
  $installCmd = 'bash -lc ''for i in $(seq 1 36); do if ! fuser /var/lib/dpkg/lock-frontend >/dev/null 2>&1 && ! fuser /var/lib/apt/lists/lock >/dev/null 2>&1; then break; fi; echo APT_LOCK_WAIT=$i; sleep 5; done; bash /tmp/fleetmanager-linux-deploy/install-worker-ubuntu.sh /tmp/fleetmanager-agent-autoheal'''
  Invoke-Remote $installCmd
}
else {
  Write-Output 'AGENT_ACTION=reuse_existing_install'
}

$configOk = $false
$remoteConfigObject = Get-RemoteConfigObject
if ($remoteConfigObject -and $remoteConfigObject.Agent) {
  $backendMatches = $remoteConfigObject.Agent.BackendBaseUrl -eq $apiBase
  $nodeIdLooksValid = $remoteConfigObject.Agent.NodeId -and $remoteConfigObject.Agent.NodeId -ne '00000000-0000-0000-0000-000000000000'
  if ($backendMatches -and $nodeIdLooksValid) {
    $configOk = $true
  }
}

$configCheck = if ($configOk) { 'CONFIG_OK=1' } else { 'CONFIG_OK=0' }
Write-Output $configCheck

if ($configCheck -match 'CONFIG_OK=0' -and $nodeId) {
  Write-Output 'CONFIG_ACTION=write_appsettings'
  Write-AgentConfig -ResolvedNodeId $nodeId
}
elseif ($configCheck -match 'CONFIG_OK=0') {
  Write-Output 'CONFIG_ACTION=NODE_NOT_FOUND_RETRY'
  try {
    $authBody = @{ password = $apiPassword } | ConvertTo-Json -Compress
    $token = (Invoke-RestMethod -Uri ($apiBase + '/api/auth/token') -Method Post -ContentType 'application/json' -Body $authBody -TimeoutSec 8).token
    $hdr = @{ Authorization = ('Bearer ' + $token) }
    $nodes = @(Invoke-RestMethod -Uri ($apiBase + '/api/nodes') -Headers $hdr -TimeoutSec 8)
    $matched = @($nodes | Where-Object { $_.ipAddress -eq $ip } | Select-Object -First 1)
    if ($matched) {
      $nodeId = $matched.id
      Write-Output ('MATCHED_NODE_ID_RETRY=' + $nodeId)
      Write-AgentConfig -ResolvedNodeId $nodeId
      Write-Output 'CONFIG_ACTION=write_appsettings_after_retry'
    }
    else {
      Write-Output 'MATCHED_NODE_ID_RETRY=NOT_FOUND'
    }
  }
  catch {
    Write-Output ('API_DISCOVERY_RETRY_ERROR=' + $_.Exception.Message)
  }

  if (-not $nodeId) {
    $remoteNodeId = Get-RemoteConfiguredNodeId
    if (-not [string]::IsNullOrWhiteSpace($remoteNodeId)) {
      Write-Output ('REMOTE_CONFIGURED_NODE_ID=' + $remoteNodeId)
      Write-AgentConfig -ResolvedNodeId $remoteNodeId
      Write-Output 'CONFIG_ACTION=write_appsettings_using_remote_nodeid'
    }
  }
}

Set-RemoteBrowserExtensionsOverride -Extensions $browserExtensions

$finalCmd = 'bash -lc ''systemctl daemon-reload; systemctl restart fleetmanager-agent; sleep 3; echo API_SERVICE=$(systemctl is-active fleetmanager-api 2>/dev/null || true); echo AGENT_SERVICE=$(systemctl is-active fleetmanager-agent 2>/dev/null || true); journalctl -u fleetmanager-agent --no-pager -n 20'''

$final = (Invoke-Remote $finalCmd) | Out-String
Write-Output $final.Trim()

$agentActive = $final -match 'AGENT_SERVICE=active'
if ($agentActive -and $token -and $nodeId) {
  Start-Sleep -Seconds 3
  try {
    $hdr = @{ Authorization = ('Bearer ' + $token) }
    $node = Invoke-RestMethod -Uri ($apiBase + '/api/nodes/' + $nodeId) -Headers $hdr -TimeoutSec 8
    Write-Output ('NODE_STATUS=' + $node.status)
    Write-Output ('NODE_LAST_HEARTBEAT=' + $node.lastHeartbeatAtUtc)
    if ($node.status -eq 'Online') {
      Write-Output 'RESULT=CONNECTED_AND_HEALTHY'
      exit 0
    }
  }
  catch {
    Write-Output ('NODE_CHECK_ERROR=' + $_.Exception.Message)
  }
}

if ($agentActive) {
  Write-Output 'RESULT=AGENT_ACTIVE_BUT_API_PARTIAL'
  Write-Output 'ACTION=Keep same server record and same credentials; re-enter info only if SSH/API authentication fails.'
  exit 0
}

Write-Output 'RESULT=FAILED_NEEDS_REENTER'
Write-Output 'ACTION=Re-enter VPS info only when credentials changed or connection cannot be authenticated. Do not delete the server record automatically.'
exit 1
