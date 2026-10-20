# HomeCtrl 客户端安装脚本（需在【管理员】PowerShell 中运行）
#
# 用法示例（服务名可自定义以伪装成系统服务）：
#   .\install-client.ps1 -ServiceName WksHealthSvc `
#       -DisplayName "Workstation Health Service" `
#       -Description "Workstation health and maintenance" `
#       -BaseUrl http://192.168.1.10:8080 -MachineId 1 -Secret <服务器生成的密钥>
#
# 步骤：发布 Agent/UI → 写配置 → 注册服务（含崩溃自动重启）→ UI 加入开机自启
param(
    [string]$ServiceName = "HomeCtrlAgent",
    [string]$DisplayName = "HomeCtrl Agent",
    [string]$Description = "HomeCtrl Agent",
    [string]$BaseUrl = "http://192.168.1.10:8080",
    [string]$MachineId = "1",
    [string]$Secret = "",
    [string]$InstallDir = "C:\ProgramData\HomeCtrl"
)

$ErrorActionPreference = "Stop"

if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent())
        .IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "请以管理员身份运行此脚本"
}
if ([string]::IsNullOrWhiteSpace($Secret)) {
    throw "必须提供 -Secret（在服务器管理端创建机器后获得）"
}

$repo = Split-Path -Parent $PSScriptRoot

# 1) 停止旧版本（服务/UI 运行时文件被占用，必须先停再发布，否则重装失败）
Write-Host ">> 停止旧版本 ..."
Get-Process HomeCtrl.LockScreen -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
if (Get-Service $ServiceName -ErrorAction SilentlyContinue) {
    sc.exe stop $ServiceName | Out-Null
    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 2
}

# 2) 发布
Write-Host ">> 发布 Agent ..."
dotnet publish (Join-Path $repo "src\client\HomeCtrl.Agent") -c Release -o "$InstallDir\agent"
if ($LASTEXITCODE -ne 0) { throw "Agent 发布失败（需已安装 .NET 8 SDK）" }

Write-Host ">> 发布 LockScreen UI ..."
dotnet publish (Join-Path $repo "src\client\HomeCtrl.LockScreen") -c Release -o "$InstallDir\ui"
if ($LASTEXITCODE -ne 0) { throw "LockScreen 发布失败" }

# 3) 写配置
@{
    Agent            = @{ ServiceName = $ServiceName }
    Server           = @{ BaseUrl = $BaseUrl; MachineId = $MachineId; Secret = $Secret }
    HeartbeatSeconds = 10
} | ConvertTo-Json -Depth 5 | Set-Content "$InstallDir\agent\appsettings.json" -Encoding UTF8

# 4) 注册 Windows 服务（名称即在此伪装）+ 崩溃自动重启
sc.exe create $ServiceName binPath= "$InstallDir\agent\HomeCtrl.Agent.exe" start= auto DisplayName= $DisplayName
sc.exe description $ServiceName $Description
sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/10000/restart/60000
sc.exe start $ServiceName

# 5) 离线缓存目录：仅 SYSTEM/管理员可访问（kit 含 PIN 哈希，防标准用户读取）
New-Item -ItemType Directory -Force "$InstallDir\offline" | Out-Null
icacls "$InstallDir\offline" /inheritance:r /grant "SYSTEM:(OI)(CI)F" /grant "Administrators:(OI)(CI)F" | Out-Null

# 6) 锁屏 UI 加入当前用户开机自启（共享 Windows 账户需先配置自动登录）
$startup = [Environment]::GetFolderPath("Startup")
$shell = New-Object -ComObject WScript.Shell
$lnk = $shell.CreateShortcut((Join-Path $startup "HomeCtrlUI.lnk"))
$lnk.TargetPath = "$InstallDir\ui\HomeCtrl.LockScreen.exe"
$lnk.WorkingDirectory = "$InstallDir\ui"
$lnk.Save()

Write-Host ""
Write-Host "安装完成：服务 [$DisplayName] ($ServiceName)，UI 已加入当前用户自启。"
Write-Host "提示："
Write-Host " 1. 共享 Windows 账户建议配置自动登录（netplwiz），账户类型必须是【标准用户】。"
Write-Host " 2. P1 加固项（按需手动）：策略禁用任务管理器/注册表编辑器、限制改系统时间。"
