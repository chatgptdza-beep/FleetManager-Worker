[CmdletBinding()]
param(
    [string]$ApiBaseUrl = 'http://localhost:5000',
    [string]$AdminPassword = 'FleetManager-DevOnly-ChangeMe!',
    [Parameter(Mandatory = $true)]
    [string]$NodeId,
    [Parameter(Mandatory = $true)]
    [string]$Email,
    [Parameter(Mandatory = $true)]
    [string]$Username,
    [string]$Status = 'Stable'
)

$t = (Invoke-RestMethod -Uri "$ApiBaseUrl/api/auth/token" -Method Post -ContentType 'application/json' -Body (@{ password = $AdminPassword } | ConvertTo-Json)).token
$h = @{ Authorization = "Bearer $t" }
$body = @{
    nodeId = $NodeId
    email = $Email
    username = $Username
    status = $Status
} | ConvertTo-Json

$a = Invoke-RestMethod -Uri "$ApiBaseUrl/api/accounts" -Method Post -Headers $h -ContentType 'application/json' -Body $body
Write-Host "Account ID: $($a.id)"
Write-Host "Email: $($a.email)"
Write-Host "Username: $($a.username)"
Write-Host "Status: $($a.status)"
