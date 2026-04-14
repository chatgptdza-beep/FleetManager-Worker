$ErrorActionPreference = 'Stop'

Set-Location "c:\Users\fayss\Desktop\project vps+manager\md file\01_full_project_latest"
New-Item -ItemType Directory -Path "output\diagnostics" -Force | Out-Null
$ts = Get-Date -Format 'yyyyMMdd_HHmmss'
$outFile = "output\diagnostics\remote_viewer_diagnostic_$ts.log"

$base = $env:FLEETMANAGER_DIAG_API_BASE_URL
$vpsIp = $env:FLEETMANAGER_DIAG_VPS_IP
$pw = $env:FLEETMANAGER_DIAG_SSH_PASSWORD
$adminPassword = $env:FLEETMANAGER_DIAG_ADMIN_PASSWORD

if ([string]::IsNullOrWhiteSpace($base)) {
  throw 'Missing FLEETMANAGER_DIAG_API_BASE_URL environment variable.'
}

if ([string]::IsNullOrWhiteSpace($vpsIp)) {
  throw 'Missing FLEETMANAGER_DIAG_VPS_IP environment variable.'
}

if ([string]::IsNullOrWhiteSpace($pw)) {
  throw 'Missing FLEETMANAGER_DIAG_SSH_PASSWORD environment variable.'
}

if ([string]::IsNullOrWhiteSpace($adminPassword)) {
  throw 'Missing FLEETMANAGER_DIAG_ADMIN_PASSWORD environment variable.'
}

$authBody = @{ password = $adminPassword } | ConvertTo-Json -Compress
$token = (Invoke-RestMethod -Uri ($base + '/api/auth/token') -Method Post -ContentType 'application/json' -Body $authBody).token
$hdr = @{ Authorization = ('Bearer ' + $token) }

$nodes = Invoke-RestMethod -Uri ($base + '/api/nodes') -Headers $hdr
$node = $nodes | Select-Object -First 1
$accounts = Invoke-RestMethod -Uri ($base + '/api/accounts?nodeId=' + $node.id) -Headers $hdr
$target = $accounts | Select-Object -First 1

@(
  '=== Remote Viewer Diagnostic ===',
  'UTC: ' + (Get-Date).ToUniversalTime().ToString('o'),
  'API: ' + $base,
  'NodeId: ' + $node.id,
  'NodeName: ' + $node.name,
  'NodeStatus: ' + $node.status,
  'NodeHeartbeat: ' + $node.lastHeartbeatAtUtc,
  'AccountCount: ' + @($accounts).Count,
  'TargetAccountId: ' + $target.id,
  'TargetEmail: ' + $target.email,
  'TargetStatus: ' + $target.status,
  ''
) | Set-Content -Path $outFile

if ($target) {
  $payload = @{ accountId = $target.id; email = $target.email; username = $target.username } | ConvertTo-Json -Compress
  $cmdReq = @{ commandType = 'OpenAssignedSession'; payloadJson = $payload } | ConvertTo-Json -Compress
  $cmdId = (Invoke-RestMethod -Uri ($base + '/api/nodes/' + $node.id + '/commands') -Method Post -Headers $hdr -ContentType 'application/json' -Body $cmdReq).commandId
  Add-Content $outFile ('Dispatched OpenAssignedSession CommandId: ' + $cmdId)

  $final = $null
  for ($i = 0; $i -lt 30; $i++) {
    $c = Invoke-RestMethod -Uri ($base + '/api/nodes/' + $node.id + '/commands/' + $cmdId) -Headers $hdr
    if ($c.status -in @('Executed', 'Failed', 'TimedOut')) {
      $final = $c
      break
    }
    Start-Sleep -Milliseconds 700
  }
  if (-not $final) {
    $final = Invoke-RestMethod -Uri ($base + '/api/nodes/' + $node.id + '/commands/' + $cmdId) -Headers $hdr
  }
  Add-Content $outFile ('Final Command: ' + ($final | ConvertTo-Json -Depth 6))
}

Add-Content $outFile "`n=== VPS SERVICE STATUS ==="
& "C:\Program Files\PuTTY\plink.exe" -ssh -batch -pw $pw ("root@{0}" -f $vpsIp) 'echo agent=$(systemctl is-active fleetmanager-agent); echo api=$(systemctl is-active fleetmanager-api); ss -lntp || true' 2>&1 | Add-Content $outFile

Add-Content $outFile "`n=== AGENT JOURNAL (last 200) ==="
& "C:\Program Files\PuTTY\plink.exe" -ssh -batch -pw $pw ("root@{0}" -f $vpsIp) 'journalctl -u fleetmanager-agent --no-pager -n 200' 2>&1 | Add-Content $outFile

Add-Content $outFile "`n=== API JOURNAL (last 200) ==="
& "C:\Program Files\PuTTY\plink.exe" -ssh -batch -pw $pw ("root@{0}" -f $vpsIp) 'journalctl -u fleetmanager-api --no-pager -n 200' 2>&1 | Add-Content $outFile

if ($target) {
  Add-Content $outFile "`n=== SESSION LOGS FOR TARGET ACCOUNT ==="
  $rid = $target.id
  $rcmd = ('acc="{0}"; echo browser.log; tail -n 120 /var/lib/fleetmanager/sessions/$acc/browser.log 2>/dev/null || true; echo viewer.log; tail -n 120 /var/lib/fleetmanager/sessions/$acc/viewer.log 2>/dev/null || true; echo account.log; tail -n 120 /var/lib/fleetmanager/logs/$acc.log 2>/dev/null || true' -f $rid)
  & "C:\Program Files\PuTTY\plink.exe" -ssh -batch -pw $pw ("root@{0}" -f $vpsIp) $rcmd 2>&1 | Add-Content $outFile
}

Write-Output $outFile
