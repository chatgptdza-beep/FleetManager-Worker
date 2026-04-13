$b = 'http://localhost:5000'
$t = (Invoke-RestMethod -Uri "$b/api/auth/token" -Method Post -ContentType 'application/json' -Body '{"password":"Admin@FleetMgr2026!"}').token
$h = @{ Authorization = "Bearer $t" }
$body = '{"nodeId":"1b43bdb5-93c0-4b29-8c14-fb6ccd0d1905","email":"fayssalb28@gmail.com","username":"fayssaldz","status":"Stable"}'
$a = Invoke-RestMethod -Uri "$b/api/accounts" -Method Post -Headers $h -ContentType 'application/json' -Body $body
Write-Host "Account ID: $($a.id)"
Write-Host "Email: $($a.email)"
Write-Host "Username: $($a.username)"
Write-Host "Status: $($a.status)"
