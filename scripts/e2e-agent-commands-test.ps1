# TimeBank agent command-execution test: run the REAL Agent binary, then drive
# it through admin commands (the paths HeartbeatWorker.ExecuteCommandAsync runs):
#   lock -> unlock -> message -> UI watchdog auto-logout
# NOTE: logoff/shutdown/reboot are destructive and NOT tested here (need a VM).
# Usage: powershell -File scripts\e2e-agent-commands-test.ps1
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

function Stop-TestAgents {
    Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" |
        Where-Object { $_.CommandLine -match "TimeBank\.Agent" } |
        ForEach-Object { Write-Host "  killing leftover agent pid=$($_.ProcessId)"; Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
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
            if ($obj.PSObject.Properties["event"]) { continue }  # push event - skip
            if ($obj.PSObject.Properties["id"]) { $writer.Dispose(); $reader.Dispose(); return $obj }
        }
        throw "timeout waiting for reply"
    } finally {
        if ($pipe) { $pipe.Dispose() }
    }
}

# Poll a condition with fresh pipe getState calls (each call resets the UI watchdog timer)
function Wait-State([scriptblock]$cond, [int]$timeoutSec) {
    $deadline = [DateTime]::UtcNow.AddSeconds($timeoutSec)
    while ([DateTime]::UtcNow -lt $deadline) {
        $st = Pipe-Call "getState"
        if (& $cond $st.data) { return $st.data }
        Start-Sleep -Milliseconds 800
    }
    return $null
}

Write-Host "== cleanup leftovers =="
Stop-TestAgents

# --- setup: admin login, machine, person ---
$ready = $false
for ($i = 0; $i -lt 30; $i++) {
    try { Invoke-RestMethod "$Server/" -TimeoutSec 5 | Out-Null; $ready = $true; break } catch { Start-Sleep 1 }
}
if (-not $ready) { throw "Server $Server is not ready" }

$admin = Invoke-RestMethod -Method Post "$Server/api/admin/login" -ContentType "application/json" -Body (@{password=$AdminPassword} | ConvertTo-Json -Compress)
$auth = @{ Authorization = "Bearer $($admin.token)" }
$mach = Invoke-RestMethod -Method Post "$Server/api/admin/machines" -Headers $auth -ContentType "application/json" -Body (@{name="cmd-e2e-$(Get-Random)"} | ConvertTo-Json -Compress)
$card = "C$(Get-Random)"
$name = "CMD-$(Get-Random)"
$acct = Invoke-RestMethod -Method Post "$Server/api/admin/persons" -Headers $auth -ContentType "application/json" -Body (@{cardNo=$card; displayName=$name; pin="5555"; initialSeconds=600} | ConvertTo-Json -Compress)
Check "setup (machine=$($mach.id) person=$name)" ([bool]$mach.ok -and [bool]$acct.ok)

# --- start real agent, short heartbeat, short watchdog (15s) ---
$env:Server__BaseUrl = $Server
$env:Server__MachineId = "$($mach.id)"
$env:Server__Secret = $mach.secret
$env:HeartbeatSeconds = "3"
$env:UiWatchdogSeconds = "15"
$agent = Start-Process -FilePath $Dotnet -ArgumentList @("$PSScriptRoot\..\src\client\TimeBank.Agent\bin\Debug\net8.0\TimeBank.Agent.dll") -PassThru -WindowStyle Hidden -RedirectStandardOutput "$env:TEMP\agent-cmd-out.log" -RedirectStandardError "$env:TEMP\agent-cmd-err.log"
Write-Host "  agent pid=$($agent.Id)"

try {
    # --- 1. wait for pipe + login ---
    $st = $null
    for ($i = 0; $i -lt 20; $i++) { Start-Sleep -Milliseconds 500; try { $st = Pipe-Call "getState"; break } catch {} }
    Check "pipe up, initial locked" ($st.data.locked -eq $true)

    $login = Pipe-Call "login" @{ cardNo = $card; pin = "5555" }
    Check "pipe login ok" ($login.data.ok -eq $true)
    $st = Pipe-Call "getState"
    Check "unlocked, session active" ($st.data.locked -eq $false -and $null -ne $st.data.sessionId)

    # --- 2. admin LOCK command -> agent must lock within a heartbeat ---
    Invoke-RestMethod -Method Post "$Server/api/admin/machines/$($mach.id)/command" -Headers $auth -ContentType "application/json" -Body (@{action="lock"} | ConvertTo-Json -Compress) | Out-Null
    $st = Wait-State { param($d) $d.locked -eq $true } 10
    Check "admin lock command executed" ($null -ne $st -and $st.locked -eq $true)

    # --- 3. admin UNLOCK command ---
    Invoke-RestMethod -Method Post "$Server/api/admin/machines/$($mach.id)/command" -Headers $auth -ContentType "application/json" -Body (@{action="unlock"} | ConvertTo-Json -Compress) | Out-Null
    $st = Wait-State { param($d) $d.locked -eq $false } 10
    Check "admin unlock command executed" ($null -ne $st -and $st.locked -eq $false)

    # --- 4. admin MESSAGE command ---
    $msgText = "cmd-e2e-$(Get-Random)"
    Invoke-RestMethod -Method Post "$Server/api/admin/machines/$($mach.id)/command" -Headers $auth -ContentType "application/json" -Body (@{action="message"; text=$msgText} | ConvertTo-Json -Compress) | Out-Null
    $st = Wait-State { param($d) $d.message -eq $msgText } 10
    Check "admin message command delivered" ($null -ne $st -and $st.message -eq $msgText)

    # --- 5. UI watchdog: stop touching the pipe, session must auto-end (~15s + check cycle) ---
    Write-Host "  waiting 30s without pipe traffic (watchdog should fire at ~15-25s)..."
    Start-Sleep 30
    $st = Pipe-Call "getState"
    Check "watchdog ended session (locked again)" ($st.data.locked -eq $true -and $null -eq $st.data.sessionId)

    $sessions = Invoke-RestMethod "$Server/api/admin/sessions?limit=10" -Headers $auth
    $target = $sessions | Where-Object { $_.person -eq $name } | Select-Object -First 1
    Check "session ended server-side (reason=$($target.endReason))" ($target.status -eq "ended" -and $target.endReason -eq "logout")
} finally {
    Stop-Process -Id $agent.Id -Force -ErrorAction SilentlyContinue
    Stop-TestAgents
    $env:Server__BaseUrl = $null; $env:Server__MachineId = $null; $env:Server__Secret = $null
    $env:HeartbeatSeconds = $null; $env:UiWatchdogSeconds = $null
}

Write-Host "`n================================"
Write-Host "Result: $pass passed, $fail failed"
if ($fail -gt 0) {
    Write-Host "--- agent stderr ---"; Get-Content "$env:TEMP\agent-cmd-err.log" -ErrorAction SilentlyContinue | Select-Object -First 20
    exit 1
}
