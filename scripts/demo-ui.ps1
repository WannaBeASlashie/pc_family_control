# TimeBank UI demo: prepare test data on the server, start the REAL Agent,
# then launch the LockScreen UI in TEST mode (ESC exits - dev machine only).
# Test mode is enabled ONLY by the HOMECTRL_TEST=1 env var; the installed
# client never sets it, so production locks stay non-closable.
# Usage: powershell -File scripts\demo-ui.ps1 [-Server http://127.0.0.1:8080]
param(
    [string]$Server = "http://127.0.0.1:8080",
    [string]$AdminPassword = "changeme",
    [string]$CardNo = "001",
    [string]$Pin = "1234",
    [string]$DisplayName = "Demo Kid",
    [long]$InitialSeconds = 900,
    [string]$Dotnet = "C:\Program Files\dotnet\dotnet.exe"
)
$ErrorActionPreference = "Stop"
[System.Net.WebRequest]::DefaultWebProxy = $null

# --- wait for server ---
$ready = $false
for ($i = 0; $i -lt 30; $i++) {
    try { Invoke-RestMethod "$Server/" -TimeoutSec 5 | Out-Null; $ready = $true; break } catch { Start-Sleep 1 }
}
if (-not $ready) { throw "Server not ready at $Server (start it first)" }

$admin = Invoke-RestMethod -Method Post "$Server/api/admin/login" -ContentType "application/json" -Body (@{password=$AdminPassword} | ConvertTo-Json -Compress)
$auth = @{ Authorization = "Bearer $($admin.token)" }

# --- machine: reuse DemoPC or create ---
$machines = Invoke-RestMethod "$Server/api/admin/machines" -Headers $auth
$mach = $machines | Where-Object { $_.name -eq "DemoPC" } | Select-Object -First 1
if (-not $mach) {
    $mach = Invoke-RestMethod -Method Post "$Server/api/admin/machines" -Headers $auth -ContentType "application/json" -Body (@{name="DemoPC"} | ConvertTo-Json -Compress)
}
Write-Host "machine: DemoPC id=$($mach.id)"

# --- person: create or top up ---
$persons = Invoke-RestMethod "$Server/api/admin/persons" -Headers $auth
$acct = $persons | Where-Object { $_.cardNo -eq $CardNo } | Select-Object -First 1
if (-not $acct) {
    Invoke-RestMethod -Method Post "$Server/api/admin/persons" -Headers $auth -ContentType "application/json" -Body (@{cardNo=$CardNo; displayName=$DisplayName; pin=$Pin; initialSeconds=$InitialSeconds} | ConvertTo-Json -Compress) | Out-Null
    $acct = Invoke-RestMethod "$Server/api/admin/persons" -Headers $auth | Where-Object { $_.cardNo -eq $CardNo } | Select-Object -First 1
} elseif ($acct.balanceSeconds -le 0) {
    Invoke-RestMethod -Method Post "$Server/api/admin/persons/$($acct.id)/balance" -Headers $auth -ContentType "application/json" -Body (@{deltaSeconds=$InitialSeconds; note="demo top-up"} | ConvertTo-Json -Compress) | Out-Null
    $acct = Invoke-RestMethod "$Server/api/admin/persons" -Headers $auth | Where-Object { $_.cardNo -eq $CardNo } | Select-Object -First 1
}
Write-Host "person : card=$CardNo pin=$Pin balance=$($acct.balanceSeconds)s"

# --- clean leftovers: best-effort logout over the pipe first, then kill the agent ---
# (ESC only closes the UI; without logout the session would resume unlocked on restart)
function Try-PipeLogout {
    try {
        $pipe = [System.IO.Pipes.NamedPipeClientStream]::new(".", "timebank.agent", [System.IO.Pipes.PipeDirection]::InOut)
        try {
            $pipe.Connect(1500)
            $writer = [System.IO.StreamWriter]::new($pipe, [System.Text.Encoding]::UTF8)
            $writer.AutoFlush = $true
            $reader = [System.IO.StreamReader]::new($pipe, [System.Text.Encoding]::UTF8)
            $writer.WriteLine('{"id":1,"cmd":"logout"}')
            $null = $reader.ReadLine()
            $writer.Dispose(); $reader.Dispose()
            Write-Host "old agent: session logged out"
        } finally { $pipe.Dispose() }
    } catch { }
}
Try-PipeLogout

Get-Process TimeBank.LockScreen -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" |
    Where-Object { $_.CommandLine -match "TimeBank\.Agent" } |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
Start-Sleep 1

# --- start the real agent ---
$env:Server__BaseUrl = $Server
$env:Server__MachineId = "$($mach.id)"
$env:Server__Secret = $mach.secret
$env:HeartbeatSeconds = "3"
$agent = Start-Process -FilePath $Dotnet -ArgumentList @("$PSScriptRoot\..\src\client\TimeBank.Agent\bin\Debug\net8.0\TimeBank.Agent.dll") -PassThru -WindowStyle Hidden -RedirectStandardOutput "$env:TEMP\demo-agent-out.log" -RedirectStandardError "$env:TEMP\demo-agent-err.log"
Write-Host "agent  : pid=$($agent.Id)"

# --- wait for the agent pipe ---
$up = $false
for ($i = 0; $i -lt 20; $i++) {
    Start-Sleep -Milliseconds 500
    if ([System.IO.Directory]::GetFiles("\\.\pipe\") -contains "\\.\pipe\timebank.agent") { $up = $true; break }
}
if (-not $up) { throw "agent pipe did not come up (see $env:TEMP\demo-agent-err.log)" }

# --- launch UI in TEST mode ---
$env:HOMECTRL_TEST = "1"
$ui = Start-Process -FilePath "$PSScriptRoot\..\src\client\TimeBank.LockScreen\bin\Debug\net8.0-windows\TimeBank.LockScreen.exe" -PassThru
$env:HOMECTRL_TEST = $null

Write-Host ""
Write-Host "=== UI launched (pid=$($ui.Id)) ===" -ForegroundColor Cyan
Write-Host "  card no : $CardNo"
Write-Host "  pin     : $Pin"
Write-Host "  balance : $($acct.balanceSeconds) seconds"
Write-Host "  ESC     : exit UI (test mode only)"
Write-Host "  note    : ESC without logout -> agent watchdog ends the session in ~60s"
