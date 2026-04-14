$ErrorActionPreference = 'Stop'

$base = $env:FLEETMANAGER_E2E_API_BASE_URL
$adminPassword = $env:FLEETMANAGER_E2E_ADMIN_PASSWORD

if ([string]::IsNullOrWhiteSpace($base)) {
  throw 'Missing FLEETMANAGER_E2E_API_BASE_URL environment variable.'
}

if ([string]::IsNullOrWhiteSpace($adminPassword)) {
  throw 'Missing FLEETMANAGER_E2E_ADMIN_PASSWORD environment variable.'
}

$authBody = @{ password = $adminPassword } | ConvertTo-Json -Compress
$token = (Invoke-RestMethod -Uri ($base + '/api/auth/token') -Method Post -ContentType 'application/json' -Body $authBody).token
$hdr = @{ Authorization = ('Bearer ' + $token) }

$nodes = Invoke-RestMethod -Uri ($base + '/api/nodes') -Headers $hdr
$node = @($nodes | Where-Object { $_.status -eq 'Online' } | Select-Object -First 1)
if (-not $node) { $node = @($nodes | Select-Object -First 1) }
if (-not $node) { throw 'No node found.' }

$accounts = Invoke-RestMethod -Uri ($base + '/api/accounts?nodeId=' + $node.id) -Headers $hdr
$target = @($accounts | Select-Object -First 1)
if (-not $target) { throw 'No account found to test.' }

Write-Output ('Node=' + $node.id + ' Status=' + $node.status)
Write-Output ('Account=' + $target.id + ' Email=' + $target.email + ' Status=' + $target.status)

$payload = @{ accountId = $target.id; email = $target.email; username = $target.username } | ConvertTo-Json -Compress

# 1) OpenAssignedSession
$openReq = @{ commandType = 'OpenAssignedSession'; payloadJson = $payload } | ConvertTo-Json -Compress
$openId = (Invoke-RestMethod -Uri ($base + '/api/nodes/' + $node.id + '/commands') -Method Post -Headers $hdr -ContentType 'application/json' -Body $openReq).commandId
Write-Output ('OpenAssignedSession CommandId=' + $openId)

$openFinal = $null
for ($i = 0; $i -lt 40; $i++) {
  $c = Invoke-RestMethod -Uri ($base + '/api/nodes/' + $node.id + '/commands/' + $openId) -Headers $hdr
  if ($c.status -in @('Executed', 'Failed', 'TimedOut')) {
    $openFinal = $c
    break
  }
  Start-Sleep -Milliseconds 800
}
if (-not $openFinal) { $openFinal = Invoke-RestMethod -Uri ($base + '/api/nodes/' + $node.id + '/commands/' + $openId) -Headers $hdr }
Write-Output ('OpenAssignedSession Final=' + $openFinal.status)
Write-Output ('OpenAssignedSession Result=' + ($openFinal.resultMessage -replace "`r`n", ' | '))

# 2) FetchSessionLogs
$logReq = @{ commandType = 'FetchSessionLogs'; payloadJson = $payload } | ConvertTo-Json -Compress
$logId = (Invoke-RestMethod -Uri ($base + '/api/nodes/' + $node.id + '/commands') -Method Post -Headers $hdr -ContentType 'application/json' -Body $logReq).commandId
Write-Output ('FetchSessionLogs CommandId=' + $logId)

$logFinal = $null
for ($i = 0; $i -lt 30; $i++) {
  $c = Invoke-RestMethod -Uri ($base + '/api/nodes/' + $node.id + '/commands/' + $logId) -Headers $hdr
  if ($c.status -in @('Executed', 'Failed', 'TimedOut')) {
    $logFinal = $c
    break
  }
  Start-Sleep -Milliseconds 800
}
if (-not $logFinal) { $logFinal = Invoke-RestMethod -Uri ($base + '/api/nodes/' + $node.id + '/commands/' + $logId) -Headers $hdr }
Write-Output ('FetchSessionLogs Final=' + $logFinal.status)
if ($logFinal.resultMessage) {
  $preview = $logFinal.resultMessage
  if ($preview.Length -gt 400) { $preview = $preview.Substring(0, 400) }
  Write-Output ('FetchSessionLogs Preview=' + ($preview -replace "`r`n", ' | '))
}

# 3) Delete endpoint check on existing account (requested live delete test)
$delResp = Invoke-WebRequest -Uri ($base + '/api/accounts/' + $target.id) -Method Delete -Headers $hdr -UseBasicParsing
Write-Output ('DeleteStatusCode=' + [int]$delResp.StatusCode)

$accountsAfter = Invoke-RestMethod -Uri ($base + '/api/accounts?nodeId=' + $node.id) -Headers $hdr
$stillExists = @($accountsAfter | Where-Object { $_.id -eq $target.id }).Count -gt 0
Write-Output ('DeleteVerified=' + (-not $stillExists))
