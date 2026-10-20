# TimeBank 客户端安装脚本（需在【管理员】PowerShell 中运行）
#
# 用法示例（服务名可自定义以伪装成系统服务）：
#   .\install-client.ps1 -ServiceName WksHealthSvc `
#       -DisplayName "Workstation Health Service" `
#       -Description "Workstation health and maintenance" `
#       -BaseUrl http://192.168.1.10:8080 -MachineId 1 -Secret <服务器生成的密钥>
#
# 步骤：发布 Agent/UI → 写配置 → 注册服务（含崩溃自动重启）→ UI 加入开机自启
param(
    [string]$ServiceName = "TimeBankAgent",
    [string]$DisplayName = "TimeBank Agent",
    [string]$Description = "TimeBank Agent",
    [string]$BaseUrl = "http://192.168.1.10:8080",
    [string]$MachineId = "1",
    [string]$Secret = "",
    [string]$InstallDir = "C:\ProgramData\TimeBank"
)

$ErrorActionPreference = "Stop"

if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent())
        .IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Run this script as Administrator"
}
if ([string]::IsNullOrWhiteSpace($Secret)) {
    throw "Missing -Secret (create the machine in the admin console first)"
}

$repo = Split-Path -Parent $PSScriptRoot

# 1) 停止旧版本（服务/UI 运行时文件被占用，必须先停再发布，否则重装失败）
Write-Host ">> Stopping previous version ..."
Get-Process TimeBank.LockScreen -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
if (Get-Service $ServiceName -ErrorAction SilentlyContinue) {
    sc.exe stop $ServiceName | Out-Null
    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 2
}

# 2) 发布
Write-Host ">> Publishing Agent ..."
dotnet publish (Join-Path $repo "src\client\TimeBank.Agent") -c Release -o "$InstallDir\agent"
if ($LASTEXITCODE -ne 0) { throw "Agent publish failed (is .NET 8 SDK installed?)" }

Write-Host ">> Publishing LockScreen UI ..."
dotnet publish (Join-Path $repo "src\client\TimeBank.LockScreen") -c Release -o "$InstallDir\ui"
if ($LASTEXITCODE -ne 0) { throw "LockScreen publish failed" }

# 3) 写配置
@{
    Agent            = @{ ServiceName = $ServiceName }
    Server           = @{ BaseUrl = $BaseUrl; MachineId = $MachineId; Secret = $Secret }
    HeartbeatSeconds = 10
} | ConvertTo-Json -Depth 5 | Set-Content "$InstallDir\agent\appsettings.json" -Encoding UTF8

# 4) 注册 Windows 服务（名称即在此伪装）+ 崩溃自动重启
sc.exe create $ServiceName binPath= "$InstallDir\agent\TimeBank.Agent.exe" start= auto DisplayName= $DisplayName
sc.exe description $ServiceName $Description
sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/10000/restart/60000
sc.exe start $ServiceName

# 5) Offline cache dir: SYSTEM/Administrators only (kit contains PIN hashes)
New-Item -ItemType Directory -Force "$InstallDir\offline" | Out-Null
icacls "$InstallDir\offline" /inheritance:r /grant "SYSTEM:(OI)(CI)F" /grant "Administrators:(OI)(CI)F" | Out-Null

# 6) Add UI to current user startup (shared account needs auto-login)
$startup = [Environment]::GetFolderPath("Startup")
$shell = New-Object -ComObject WScript.Shell
$lnk = $shell.CreateShortcut((Join-Path $startup "TimeBankUI.lnk"))
$lnk.TargetPath = "$InstallDir\ui\TimeBank.LockScreen.exe"
$lnk.WorkingDirectory = "$InstallDir\ui"
$lnk.Save()

Write-Host ""
Write-Host "Done. Service [$DisplayName] ($ServiceName); UI added to current user startup."
Write-Host "Notes:"
Write-Host " 1. Configure auto-login for the shared Windows account (netplwiz); it MUST be a standard user."
Write-Host " 2. Lock screen hardens itself: covers all monitors + taskbar, blocks Win/Alt+Tab/Alt+F4/Ctrl+Esc."
