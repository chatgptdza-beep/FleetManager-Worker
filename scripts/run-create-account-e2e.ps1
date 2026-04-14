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

$seed = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
$email = "live.e2e.$seed@example.com"
$username = "live_e2e_$seed"

$newReq = @{
  nodeId = $node.id
  email = $email
  username = $username
  status = 'Stable'
} | ConvertTo-Json -Compress

$created = Invoke-RestMethod -Uri ($base + '/api/accounts') -Method Post -Headers $hdr -ContentType 'application/json' -Body $newReq
Write-Output ('CreatedAccount=' + $created.id + ' Email=' + $created.email + ' Username=' + $created.username + ' Status=' + $created.status)

$payload = @{ accountId = $created.id; email = $created.email; username = $created.username } | ConvertTo-Json -Compress

$openReq = @{ commandType = 'OpenAssignedSession'; payloadJson = $payload } | ConvertTo-Json -Compress
$openId = (Invoke-RestMethod -Uri ($base + '/api/nodes/' + $node.id + '/commands') -Method Post -Headers $hdr -ContentType 'application/json' -Body $openReq).commandId
Write-Output ('OpenAssignedSession CommandId=' + $openId)

$openFinal = $null
for ($i = 0; $i -lt 45; $i++) {
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

$logReq = @{ commandType = 'FetchSessionLogs'; payloadJson = $payload } | ConvertTo-Json -Compress
$logId = (Invoke-RestMethod -Uri ($base + '/api/nodes/' + $node.id + '/commands') -Method Post -Headers $hdr -ContentType 'application/json' -Body $logReq).commandId
Write-Output ('FetchSessionLogs CommandId=' + $logId)

$logFinal = $null
for ($i = 0; $i -lt 35; $i++) {
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

$delResp = Invoke-WebRequest -Uri ($base + '/api/accounts/' + $created.id) -Method Delete -Headers $hdr -UseBasicParsing
Write-Output ('DeleteStatusCode=' + [int]$delResp.StatusCode)

$accountsAfter = Invoke-RestMethod -Uri ($base + '/api/accounts?nodeId=' + $node.id) -Headers $hdr
$stillExists = @($accountsAfter | Where-Object { $_.id -eq $created.id }).Count -gt 0
Write-Output ('DeleteVerified=' + (-not $stillExists))
