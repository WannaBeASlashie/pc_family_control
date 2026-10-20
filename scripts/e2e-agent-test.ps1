# TimeBank end-to-end test: run the REAL Agent binary against the server,
# talk to it over the named pipe (same protocol as the WPF UI).
#   create machine -> start agent -> heartbeat seen -> pipe getState
#   -> pipe login (real person) -> pipe getState (unlocked) -> pipe logout
# Usage: powershell -File scripts\e2e-agent-test.ps1
param(
    [string]$Server = "http://127.0.0.1:8080",
    [string]$AdminPassword = "changeme",
    [string]$Dotnet = "C:\Program Files\dotnet\dotnet.exe"
)
$ErrorActionPreference = "Stop"
$script:pass = 0; $script:fail = 0
[System.Net.WebRequest]::DefaultWebProxy = $null

function Check([string]$name, [bool]$cond) {
    if ($cond) { $script:pass++; Write-Host "  [PASS] $name" -ForegroundColor Green }
    else       { $script:fail++; Write-Host "  [FAIL] $name" -ForegroundColor Red }
}

# Kill leftover test agents (started as dotnet.exe, process name is NOT TimeBank.Agent!)
function Stop-TestAgents {
    Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" |
        Where-Object { $_.CommandLine -match "TimeBank\.Agent" } |
        ForEach-Object { Write-Host "  killing leftover agent pid=$($_.ProcessId)"; Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
}

# Named pipe call (same protocol as the WPF UI). NOTE: param must not be named $args!
# The agent may push state events BEFORE the reply (e.g. login fires a state push);
# skip event lines and wait for the line carrying our reply id.
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
            if ($obj.PSObject.Properties["event"]) { continue }  # push event - skip
            if ($obj.PSObject.Properties["id"]) { $writer.Dispose(); $reader.Dispose(); return $obj }
        }
        throw "timeout waiting for reply"
    } finally {
        if ($pipe) { $pipe.Dispose() }
    }
}

Write-Host "== cleanup leftovers =="
Stop-TestAgents

# --- 1. create machine + person via admin API ---
$admin = Invoke-RestMethod -Method Post "$Server/api/admin/login" -ContentType "application/json" -Body (@{password=$AdminPassword} | ConvertTo-Json -Compress)
$auth = @{ Authorization = "Bearer $($admin.token)" }
$mach = Invoke-RestMethod -Method Post "$Server/api/admin/machines" -Headers $auth -ContentType "application/json" -Body (@{name="e2e-$(Get-Random)"} | ConvertTo-Json -Compress)
$card = "E$(Get-Random)"
$e2eName = "E2E-$(Get-Random)"
$acct = Invoke-RestMethod -Method Post "$Server/api/admin/persons" -Headers $auth -ContentType "application/json" -Body (@{cardNo=$card; displayName=$e2eName; pin="4321"; initialSeconds=300} | ConvertTo-Json -Compress)
Check "machine+person created" ([bool]$mach.ok -and [bool]$acct.ok)

# --- 2. start the real agent with env config ---
$env:Server__BaseUrl = $Server
$env:Server__MachineId = "$($mach.id)"
$env:Server__Secret = $mach.secret
$env:HeartbeatSeconds = "3"
$agent = Start-Process -FilePath $Dotnet -ArgumentList @("$PSScriptRoot\..\src\client\TimeBank.Agent\bin\Debug\net8.0\TimeBank.Agent.dll") -PassThru -WindowStyle Hidden -RedirectStandardOutput "$env:TEMP\agent-out.log" -RedirectStandardError "$env:TEMP\agent-err.log"
Write-Host "  agent pid=$($agent.Id), waiting for pipe..."

try {
    # --- 3. wait for pipe, then getState ---
    $state = $null
    for ($i = 0; $i -lt 20; $i++) {
        Start-Sleep -Milliseconds 500
        try { $state = Pipe-Call "getState"; break } catch {}
    }
    Check "pipe getState responds" ($null -ne $state)
    Check "agent reports server online" ($state.data.serverOnline -eq $true)
    Check "initial state locked" ($state.data.locked -eq $true)

    # --- 4. heartbeat must be flowing (machine lastSeenAt fresh) ---
    Start-Sleep 4
    $overview = Invoke-RestMethod "$Server/api/admin/overview" -Headers $auth
    $m = $overview.machines | Where-Object { $_.id -eq $mach.id }
    Check "server sees agent heartbeat" ($m.online -eq $true)

    # --- 5. login over the pipe ---
    $login = Pipe-Call "login" @{ cardNo = $card; pin = "4321" }
    Check "pipe login ok ($($login.data.person))" ($login.data.ok -eq $true)

    $state = Pipe-Call "getState"
    Check "state unlocked after login" ($state.data.locked -eq $false)
    Check "balance shown ($($state.data.balanceSeconds)s)" ($state.data.balanceSeconds -gt 0)

    # --- 6. wrong pin rejected ---
    $bad = Pipe-Call "login" @{ cardNo = $card; pin = "0000" }
    Check "bad pin rejected via pipe" ($bad.data.ok -eq $false)

    # --- 7. quick reconnect (regression: accept loop must re-listen immediately) ---
    $again = Pipe-Call "getState"
    Check "immediate reconnect works" ($null -ne $again)

    # --- 8. logout over the pipe ---
    $out = Pipe-Call "logout"
    Check "pipe logout ok" ($out.data.ok -eq $true)
    $state = Pipe-Call "getState"
    Check "state locked after logout" ($state.data.locked -eq $true)

    # --- 9. session recorded server-side ---
    $sessions = Invoke-RestMethod "$Server/api/admin/sessions?limit=10" -Headers $auth
    $target = $sessions | Where-Object { $_.person -eq $e2eName } | Select-Object -First 1
    Check "session ended on server (reason=$($target.endReason))" ($target.status -eq "ended")
} finally {
    Stop-Process -Id $agent.Id -Force -ErrorAction SilentlyContinue
    Stop-TestAgents
    $env:Server__BaseUrl = $null; $env:Server__MachineId = $null; $env:Server__Secret = $null; $env:HeartbeatSeconds = $null
}

Write-Host "`n================================"
Write-Host "Result: $pass passed, $fail failed"
if ($fail -gt 0) {
    Write-Host "--- agent stderr ---"; Get-Content "$env:TEMP\agent-err.log" -ErrorAction SilentlyContinue | Select-Object -First 20
    exit 1
}
