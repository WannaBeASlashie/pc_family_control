# TimeBank offline-mode e2e test (FR-17):
#   start server+agent -> kit synced -> kill server -> offline mode ->
#   offline login (wrong/right PIN) -> local countdown -> request rejected ->
#   offline logout -> restart server -> reconnect -> reconcile (session row,
#   balance deducted, offline_charge ledger) all verified server-side.
# NOTE: manages its own server process; leaves the server RUNNING at the end.
# Usage: powershell -File scripts\e2e-offline-test.ps1
param(
    [string]$Server = "http://127.0.0.1:8080",
    [string]$AdminPassword = "changeme",
    [string]$Dotnet = "C:\Program Files\dotnet\dotnet.exe"
)
$ErrorActionPreference = "Stop"
$script:pass = 0; $script:fail = 0
[System.Net.WebRequest]::DefaultWebProxy = $null

$repo = Split-Path -Parent $PSScriptRoot
$serverDll = "$repo\src\server\TimeBank.Server\bin\Debug\net8.0\TimeBank.Server.dll"
$serverDir = "$repo\src\server\TimeBank.Server"

function Check([string]$name, [bool]$cond) {
    if ($cond) { $script:pass++; Write-Host "  [PASS] $name" -ForegroundColor Green }
    else       { $script:fail++; Write-Host "  [FAIL] $name" -ForegroundColor Red }
}

function Stop-TestServers {
    # NOTE: server launched as "dotnet <dll>" has process name "dotnet", not TimeBank.Server!
    Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" |
        Where-Object { $_.CommandLine -match "TimeBank\.Server" } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
}

function Pipe-Call([string]$cmd, [hashtable]$extra = $null) {
    $pipe = $null
    try {
        $pipe = [System.IO.Pipes.NamedPipeClientStream]::new(".", "timebank.agent", [System.IO.Pipes.PipeDirection]::InOut)
        $pipe.Connect(5000)
        $writer = [System.IO.StreamWriter]::new($pipe, [System.Text.Encoding]::UTF8)
        $writer.AutoFlush = $true
        $reader = [System.IO.StreamReader]::new($pipe, [System.Text.Encoding]::UTF8)
        $payload = @{ id = 1; cmd = $cmd }
        if ($extra) { foreach ($k in $extra.Keys) { $payload[$k] = $extra[$k] } }
        $writer.WriteLine(($payload | ConvertTo-Json -Compress))
        $deadline = [DateTime]::UtcNow.AddSeconds(8)
        while ([DateTime]::UtcNow -lt $deadline) {
            $line = $reader.ReadLine()
            if ($null -eq $line) { throw "pipe closed before reply" }
            $obj = $null
            try { $obj = $line | ConvertFrom-Json } catch { continue }
            if ($obj.PSObject.Properties["event"]) { continue }
            if ($obj.PSObject.Properties["id"]) { $writer.Dispose(); $reader.Dispose(); return $obj }
        }
        throw "timeout waiting for reply"
    } finally {
        if ($pipe) { $pipe.Dispose() }
    }
}

function Start-TestServer {
    Start-Process -FilePath $Dotnet -ArgumentList @($serverDll) -WorkingDirectory $serverDir `
        -WindowStyle Hidden -RedirectStandardOutput "$env:TEMP\hc-off-out.log" -RedirectStandardError "$env:TEMP\hc-off-err.log"
    $ready = $false
    for ($i = 0; $i -lt 40; $i++) {
        Start-Sleep -Milliseconds 500
        try { Invoke-RestMethod "$Server/" -TimeoutSec 2 | Out-Null; $ready = $true; break } catch {}
    }
    if (-not $ready) { throw "server did not start" }
}

# ---------- clean slate ----------
Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" |
    Where-Object { $_.CommandLine -match "TimeBank\.Agent" } |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
Stop-TestServers
Start-Sleep 1
Remove-Item "C:\ProgramData\TimeBank\offline\ledger.json" -ErrorAction SilentlyContinue
Write-Host "== start server =="
Start-TestServer

# ---------- setup ----------
$admin = Invoke-RestMethod -Method Post "$Server/api/admin/login" -ContentType "application/json" -Body (@{password=$AdminPassword} | ConvertTo-Json -Compress)
$auth = @{ Authorization = "Bearer $($admin.token)" }
$mach = Invoke-RestMethod -Method Post "$Server/api/admin/machines" -Headers $auth -ContentType "application/json" -Body (@{name="off-e2e-$(Get-Random)"} | ConvertTo-Json -Compress)
$card = "O$(Get-Random)"
$name = "OFF-$(Get-Random)"
$acct = Invoke-RestMethod -Method Post "$Server/api/admin/persons" -Headers $auth -ContentType "application/json" -Body (@{cardNo=$card; displayName=$name; pin="6666"; initialSeconds=600; policyId=1} | ConvertTo-Json -Compress)
Check "setup (machine=$($mach.id) person=$name balance=600s)" ([bool]$mach.ok -and [bool]$acct.ok)

# ---------- start agent, wait online + kit ----------
$env:Server__BaseUrl = $Server
$env:Server__MachineId = "$($mach.id)"
$env:Server__Secret = $mach.secret
$env:HeartbeatSeconds = "3"
$agent = Start-Process -FilePath $Dotnet -ArgumentList @("$repo\src\client\TimeBank.Agent\bin\Debug\net8.0\TimeBank.Agent.dll") -PassThru -WindowStyle Hidden -RedirectStandardOutput "$env:TEMP\hc-off-agent-out.log" -RedirectStandardError "$env:TEMP\hc-off-agent-err.log"

try {
    $state = $null
    for ($i = 0; $i -lt 20; $i++) { Start-Sleep -Milliseconds 500; try { $state = Pipe-Call "getState"; break } catch {} }
    Check "agent up, online, not offline" ($state.data.serverOnline -eq $true -and $state.data.offline -eq $false)
    $kitPath = "C:\ProgramData\TimeBank\offline\kit.json"
    $kitOk = $false
    for ($i = 0; $i -lt 10; $i++) {
        if (Test-Path $kitPath) { $kitOk = $true; break }
        Start-Sleep -Milliseconds 500
    }
    Check "offline kit persisted" $kitOk

    # ---------- kill server -> offline mode ----------
    Write-Host "== kill server, waiting for offline mode =="
    Stop-TestServers
    $state = $null
    for ($i = 0; $i -lt 30; $i++) {
        Start-Sleep -Seconds 1
        $state = Pipe-Call "getState"
        if ($state.data.offline -eq $true) { break }
    }
    Check "agent entered offline mode" ($state.data.offline -eq $true -and $state.data.serverOnline -eq $false)

    $bad = Pipe-Call "login" @{ cardNo = $card; pin = "0000" }
    Check "wrong PIN rejected offline" ($bad.data.ok -eq $false)

    $login = Pipe-Call "login" @{ cardNo = $card; pin = "6666" }
    Check "offline login ok" ($login.data.ok -eq $true)
    $s1 = Pipe-Call "getState"
    Check "unlocked, balance from kit ($($s1.data.balanceSeconds)s <= 600)" ($s1.data.locked -eq $false -and $s1.data.sessionId -eq -1 -and $s1.data.balanceSeconds -gt 590 -and $s1.data.balanceSeconds -le 600)

    # 倒计时确认：离线 tick 的首个推送在登录后约一个心跳周期（~7s），轮询等待
    $dec = $false
    $s2 = $s1
    for ($i = 0; $i -lt 15; $i++) {
        Start-Sleep -Seconds 1
        $s2 = Pipe-Call "getState"
        if ($s2.data.balanceSeconds -lt $s1.data.balanceSeconds) { $dec = $true; break }
    }
    Check "local countdown running ($($s2.data.balanceSeconds)s < $($s1.data.balanceSeconds)s)" ($dec -eq $true)

    $req = Pipe-Call "requestTime" @{ minutes = 15 }
    Check "time request rejected offline" ($req.data.ok -eq $false)

    $out = Pipe-Call "logout"
    Check "offline logout ok" ($out.data.ok -eq $true)
    $s3 = Pipe-Call "getState"
    Check "locked again after logout" ($s3.data.locked -eq $true -and $null -eq $s3.data.sessionId)

    # ---------- restart server -> reconnect -> reconcile ----------
    Write-Host "== restart server, waiting for reconnect + reconcile =="
    Start-TestServer
    $admin = Invoke-RestMethod -Method Post "$Server/api/admin/login" -ContentType "application/json" -Body (@{password=$AdminPassword} | ConvertTo-Json -Compress)
    $auth = @{ Authorization = "Bearer $($admin.token)" }

    $state = $null
    for ($i = 0; $i -lt 30; $i++) {
        Start-Sleep -Seconds 1
        $state = Pipe-Call "getState"
        if ($state.data.serverOnline -eq $true -and $state.data.offline -eq $false) { break }
    }
    Check "reconnected and back online" ($state.data.serverOnline -eq $true -and $state.data.offline -eq $false)

    $sess = Invoke-RestMethod "$Server/api/admin/sessions?limit=10" -Headers $auth
    $row = $sess | Where-Object { $_.person -eq $name } | Select-Object -First 1
    Check "offline session reconciled (reason=$($row.endReason))" ($row.status -eq "ended" -and $row.endReason -like "offline_*")

    $ledger = Invoke-RestMethod "$Server/api/admin/ledger/$($acct.id)" -Headers $auth
    Check "offline_charge ledger row exists" (@($ledger | Where-Object { $_.kind -eq "offline_charge" }).Count -ge 1)

    $persons = Invoke-RestMethod "$Server/api/admin/persons" -Headers $auth
    $p = $persons | Where-Object { $_.cardNo -eq $card }
    Check "balance deducted ($($p.balanceSeconds)s < 600, > 550)" ($p.balanceSeconds -lt 600 -and $p.balanceSeconds -gt 550)
} finally {
    Stop-Process -Id $agent.Id -Force -ErrorAction SilentlyContinue
    Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" |
        Where-Object { $_.CommandLine -match "TimeBank\.Agent" } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    $env:Server__BaseUrl = $null; $env:Server__MachineId = $null; $env:Server__Secret = $null; $env:HeartbeatSeconds = $null
}

Write-Host "`n================================"
Write-Host "Result: $pass passed, $fail failed"
Write-Host "(server left running)"
if ($fail -gt 0) {
    Write-Host "--- agent stderr ---"; Get-Content "$env:TEMP\hc-off-agent-err.log" -ErrorAction SilentlyContinue | Select-Object -First 20
    exit 1
}
