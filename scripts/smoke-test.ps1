# HomeCtrl smoke test: full server business flow
#   admin login -> register machine -> create person (5s balance) -> login
#   -> heartbeat billing -> exhausted lock -> recharge unlock -> logout settle
# Usage: powershell -File scripts\smoke-test.ps1 [-Server http://localhost:8080]
param(
    # NOTE: use 127.0.0.1, not localhost! PS 5.1 tries IPv6 ::1 first (~2s stall
    # against our IPv4-only Kestrel), which kills requests with short timeouts.
    [string]$Server = "http://127.0.0.1:8080",
    [string]$AdminPassword = "changeme"
)
$ErrorActionPreference = "Stop"
$script:pass = 0; $script:fail = 0

# Bypass system proxy (Clash etc.) - PS 5.1 Invoke-RestMethod uses it by default
[System.Net.WebRequest]::DefaultWebProxy = $null
[System.Net.ServicePointManager]::Expect100Continue = $false

function New-Hmac([string]$secret, [string]$data) {
    $h = [System.Security.Cryptography.HMACSHA256]::new([byte[]][System.Text.Encoding]::UTF8.GetBytes($secret))
    try { -join ($h.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($data)) | ForEach-Object { $_.ToString("x2") }) }
    finally { $h.Dispose() }
}

# Simulate a controlled machine's signed request (same protocol as Agent.ServerApiClient)
function Invoke-Client([string]$path, [int]$machineId, [string]$secret, $body = $null) {
    $ts = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
    $headers = @{
        "X-Machine-Id" = "$machineId"
        "X-Timestamp"  = "$ts"
        "X-Signature"  = (New-Hmac $secret "${ts}:${path}")
    }
    $json = if ($null -ne $body) { $body | ConvertTo-Json -Compress } else { "{}" }
    Invoke-RestMethod -Method Post -Uri "$Server$path" -Headers $headers -ContentType "application/json" -Body $json
}

function Check([string]$name, [bool]$cond) {
    if ($cond) { $script:pass++; Write-Host "  [PASS] $name" -ForegroundColor Green }
    else       { $script:fail++; Write-Host "  [FAIL] $name" -ForegroundColor Red }
}

# Wait for server ready
$ready = $false
$lastErr = ""
for ($i = 0; $i -lt 30; $i++) {
    try { Invoke-RestMethod "$Server/" -TimeoutSec 5 | Out-Null; $ready = $true; break }
    catch { $lastErr = $_.Exception.Message; Start-Sleep 1 }
}
if (-not $ready) { throw "Server $Server is not ready (last error: $lastErr)" }
Write-Host "== Server ready: $Server =="

Write-Host "`n== 1. Admin login =="
$admin = Invoke-RestMethod -Method Post "$Server/api/admin/login" -ContentType "application/json" -Body (@{password=$AdminPassword} | ConvertTo-Json -Compress)
$auth = @{ Authorization = "Bearer $($admin.token)" }
Check "got admin token" ($null -ne $admin.token -and $admin.token.Length -gt 0)

Write-Host "`n== 2. Register test machine =="
$mach = Invoke-RestMethod -Method Post "$Server/api/admin/machines" -Headers $auth -ContentType "application/json" -Body (@{name="smoke-$(Get-Random)"} | ConvertTo-Json -Compress)
Check "machine created (id=$($mach.id))" ([bool]$mach.ok)

Write-Host "`n== 3. Create person (5s balance) =="
$card = "T$(Get-Random)"
$acct = Invoke-RestMethod -Method Post "$Server/api/admin/persons" -Headers $auth -ContentType "application/json" -Body (@{cardNo=$card; displayName="SmokeTest"; pin="1234"; initialSeconds=5} | ConvertTo-Json -Compress)
Check "person created (card=$card)" ([bool]$acct.ok)

Write-Host "`n== 4. Wrong PIN must be rejected =="
$bad = Invoke-Client "/api/client/login" $mach.id $mach.secret @{cardNo=$card; pin="9999"}
Check "wrong PIN rejected" (-not $bad.ok)

Write-Host "`n== 5. Normal login =="
$login = Invoke-Client "/api/client/login" $mach.id $mach.secret @{cardNo=$card; pin="1234"}
Check "login ok (session=$($login.sessionId))" ([bool]$login.ok)

Write-Host "`n== 6. Heartbeat billing =="
Start-Sleep 3
$hb = Invoke-Client "/api/client/heartbeat" $mach.id $mach.secret
Check "session active ($($hb.session.person))" ($null -ne $hb.session)
Check "balance decreasing ($($hb.balanceSeconds)s left < 5s)" ($hb.balanceSeconds -lt 5)

Write-Host "`n== 7. Exhausted -> lock command =="
Start-Sleep 6
$hb = Invoke-Client "/api/client/heartbeat" $mach.id $mach.secret
Check "balance zero" ($hb.balanceSeconds -eq 0)
Check "lock command issued" (@($hb.commands | Where-Object action -eq "lock").Count -ge 1)
$hb.commands | ForEach-Object { Invoke-Client "/api/client/command-ack" $mach.id $mach.secret @{commandId=$_.id} | Out-Null }

Write-Host "`n== 7b. Time request -> parent approves =="
$req = Invoke-Client "/api/client/request" $mach.id $mach.secret @{minutes=15}
Check "request accepted (+$($req.minutes) min)" ([bool]$req.ok)
$pending = Invoke-RestMethod "$Server/api/admin/requests?status=pending" -Headers $auth
$target2 = $pending | Where-Object { $_.person -eq "SmokeTest" } | Select-Object -First 1
Check "request listed (id=$($target2.id))" ($null -ne $target2)
Invoke-RestMethod -Method Post "$Server/api/admin/requests/$($target2.id)/decide" -Headers $auth -ContentType "application/json" -Body (@{approve=$true} | ConvertTo-Json -Compress) | Out-Null
$hb = Invoke-Client "/api/client/heartbeat" $mach.id $mach.secret
Check "balance granted ($($hb.balanceSeconds)s)" ($hb.balanceSeconds -ge 880)
Check "unlock command issued" (@($hb.commands | Where-Object action -eq "unlock").Count -ge 1)
$hb.commands | ForEach-Object { Invoke-Client "/api/client/command-ack" $mach.id $mach.secret @{commandId=$_.id} | Out-Null }

Write-Host "`n== 7c. Time request -> parent rejects (message feedback) =="
$req2 = Invoke-Client "/api/client/request" $mach.id $mach.secret @{minutes=15}
Check "second request accepted" ([bool]$req2.ok)
$pending2 = Invoke-RestMethod "$Server/api/admin/requests?status=pending" -Headers $auth
$target3 = $pending2 | Where-Object { $_.person -eq "SmokeTest" } | Select-Object -First 1
Invoke-RestMethod -Method Post "$Server/api/admin/requests/$($target3.id)/decide" -Headers $auth -ContentType "application/json" -Body (@{approve=$false} | ConvertTo-Json -Compress) | Out-Null
$hb = Invoke-Client "/api/client/heartbeat" $mach.id $mach.secret
Check "reject sends message command" (@($hb.commands | Where-Object action -eq "message").Count -ge 1)
$hb.commands | ForEach-Object { Invoke-Client "/api/client/command-ack" $mach.id $mach.secret @{commandId=$_.id} | Out-Null }

Write-Host "`n== 8. Parent recharge (session already unlocked after 7b - no unlock command needed) =="
Invoke-RestMethod -Method Post "$Server/api/admin/persons/$($acct.id)/balance" -Headers $auth -ContentType "application/json" -Body (@{deltaSeconds=600; note="smoke"} | ConvertTo-Json -Compress) | Out-Null
$hb = Invoke-Client "/api/client/heartbeat" $mach.id $mach.secret
Check "recharged (balance $($hb.balanceSeconds)s)" ($hb.balanceSeconds -ge 1480)
Check "no unlock command (session not locked)" (@($hb.commands | Where-Object action -eq "unlock").Count -eq 0)
$hb.commands | ForEach-Object { Invoke-Client "/api/client/command-ack" $mach.id $mach.secret @{commandId=$_.id} | Out-Null }

Write-Host "`n== 9. Logout settle =="
$out = Invoke-Client "/api/client/logout" $mach.id $mach.secret
Check "logout ok (charged $($out.chargedSeconds)s)" ([bool]$out.ok -and $out.chargedSeconds -gt 0)

Write-Host "`n== 10. Session & ledger records =="
$sessions = Invoke-RestMethod "$Server/api/admin/sessions?limit=5" -Headers $auth
$target = $sessions | Where-Object { $_.id -eq $login.sessionId }
Check "session ended (reason=$($target.endReason))" ($target.status -eq "ended")
$ledger = Invoke-RestMethod "$Server/api/admin/ledger/$($acct.id)" -Headers $auth
Check "ledger complete (initial+grant+charge = $($ledger.Count) rows)" ($ledger.Count -ge 3)

Write-Host "`n== 11. Policy: daily quota (60s cap, needs ~65s) =="
$pol = Invoke-RestMethod -Method Post "$Server/api/admin/policies" -Headers $auth -ContentType "application/json" -Body (@{name="quota-test"; weekdayQuotaMinutes=1; weekendQuotaMinutes=1; maxSessionMinutes=0; enabled=$true; windows=@()} | ConvertTo-Json -Compress)
Check "policy created (id=$($pol.id))" ([bool]$pol.ok)
$card2 = "Q$(Get-Random)"
$acct2 = Invoke-RestMethod -Method Post "$Server/api/admin/persons" -Headers $auth -ContentType "application/json" -Body (@{cardNo=$card2; displayName="QuotaTest"; pin="2222"; initialSeconds=3600; policyId=$pol.id} | ConvertTo-Json -Compress)
Check "quota person created" ([bool]$acct2.ok)
$login2 = Invoke-Client "/api/client/login" $mach.id $mach.secret @{cardNo=$card2; pin="2222"}
Check "login ok, effective balance capped by quota ($($login2.balanceSeconds)s <= 60s)" ($login2.ok -and $login2.balanceSeconds -gt 0 -and $login2.balanceSeconds -le 60)
Write-Host "  waiting 65s for quota to exhaust..."
Start-Sleep 65
$hb = Invoke-Client "/api/client/heartbeat" $mach.id $mach.secret
Check "effective balance hits 0 (quota)" ($hb.balanceSeconds -eq 0)
Check "lock issued with daily_quota reason" (@($hb.commands | Where-Object { $_.action -eq "lock" -and $_.payload -match "daily_quota" }).Count -ge 1)
$hb.commands | ForEach-Object { Invoke-Client "/api/client/command-ack" $mach.id $mach.secret @{commandId=$_.id} | Out-Null }
$pNow = Invoke-RestMethod "$Server/api/admin/persons" -Headers $auth
$p2 = $pNow | Where-Object { $_.cardNo -eq $card2 }
Check "raw balance untouched by quota ($($p2.balanceSeconds)s)" ($p2.balanceSeconds -gt 3500)
Invoke-Client "/api/client/logout" $mach.id $mach.secret | Out-Null
$re = Invoke-Client "/api/client/login" $mach.id $mach.secret @{cardNo=$card2; pin="2222"}
Check "re-login rejected (daily quota)" (-not $re.ok)

Write-Host "`n== 12. Policy: allowed window (closed window) =="
Invoke-Client "/api/client/logout" $mach.id $mach.secret | Out-Null   # 防御：确保机器无活动会话
$nowLocal = Get-Date
$win = @{ start = $nowLocal.AddMinutes(-60).ToString("HH:mm"); end = $nowLocal.AddMinutes(-30).ToString("HH:mm") }
$pol2 = Invoke-RestMethod -Method Post "$Server/api/admin/policies" -Headers $auth -ContentType "application/json" -Body (@{name="window-test"; weekdayQuotaMinutes=0; weekendQuotaMinutes=0; maxSessionMinutes=0; enabled=$true; windows=@($win)} | ConvertTo-Json -Compress)
Check "window policy created (id=$($pol2.id))" ([bool]$pol2.ok)
$card3 = "W$(Get-Random)"
Invoke-RestMethod -Method Post "$Server/api/admin/persons" -Headers $auth -ContentType "application/json" -Body (@{cardNo=$card3; displayName="WindowTest"; pin="3333"; initialSeconds=600; policyId=$pol2.id} | ConvertTo-Json -Compress) | Out-Null
$login3 = Invoke-Client "/api/client/login" $mach.id $mach.secret @{cardNo=$card3; pin="3333"}
Check "login rejected outside window" (-not $login3.ok)

Write-Host "`n================================"
Write-Host "Result: $pass passed, $fail failed"
if ($fail -gt 0) { exit 1 }
