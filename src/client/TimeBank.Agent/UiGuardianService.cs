using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WillBeyond.TimeBank.Agent;

/// <summary>
/// UI 进程守护：轮询检测 TimeBank.LockScreen 是否在活动控制台会话中运行，
/// 若被结束则用该会话的用户 token 重新拉起（CreateProcessAsUser，落到 winsta0\default）。
///
/// 与 60s 看门狗互补：
///  - 守护负责「秒级恢复锁屏」，被杀后立刻重新盖住桌面；
///  - 看门狗负责「UI 长时间缺席时停表」（如无人登录/反复拉起失败），防白嫖计费。
/// </summary>
public class UiGuardianService : BackgroundService
{
    private readonly ILogger<UiGuardianService> _log;
    private readonly string _uiExePath;
    private readonly string _uiProcessName;
    private readonly int _pollSeconds;
    private readonly bool _enabled;

    public UiGuardianService(IConfiguration cfg, ILogger<UiGuardianService> log)
    {
        _log = log;
        var configured = cfg["Ui:ExePath"];
        _uiExePath = !string.IsNullOrWhiteSpace(configured)
            ? Path.GetFullPath(configured)
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "ui", "TimeBank.LockScreen.exe"));
        _uiProcessName = Path.GetFileNameWithoutExtension(_uiExePath);
        _pollSeconds = Math.Clamp(cfg.GetValue("UiGuardianPollSeconds", 3), 1, 30);
        // 默认关闭：仅生产安装脚本会置为 true，避免本地/测试环境把锁屏拉到桌面
        _enabled = cfg.GetValue("UiGuardianEnabled", false);
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!_enabled)
        {
            _log.LogInformation("UI guardian disabled (UiGuardianEnabled != true)");
            return;
        }

        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!IsUiRunning())
                    Relaunch();
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "UI guardian check failed");
            }

            try { await Task.Delay(TimeSpan.FromSeconds(_pollSeconds), ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    private bool IsUiRunning()
    {
        var sessionId = Win32.GetActiveConsoleSessionId();
        foreach (var p in Process.GetProcessesByName(_uiProcessName))
        {
            try
            {
                if (p.SessionId == sessionId) return true;
            }
            catch
            {
                // 进程可能恰好退出，忽略
            }
            finally
            {
                p.Dispose();
            }
        }
        return false;
    }

    private void Relaunch()
    {
        var sessionId = Win32.GetActiveConsoleSessionId();
        if (sessionId < 0) return; // 无交互登录用户，不拉起

        if (!Win32.TryGetUserToken(sessionId, out var token))
        {
            _log.LogWarning("WTSQueryUserToken failed for session {Session} (no interactive user?)", sessionId);
            return;
        }

        try
        {
            var commandLine = $"\"{_uiExePath}\"";
            var workingDir = Path.GetDirectoryName(_uiExePath);
            if (Win32.TryCreateProcessAsUser(token, commandLine, workingDir, @"winsta0\default", out var pi))
            {
                _log.LogInformation("UI relaunched (pid {Pid}) in session {Session}", pi.dwProcessId, sessionId);
                Win32.CloseHandleSafe(pi.hProcess);
                Win32.CloseHandleSafe(pi.hThread);
            }
            else
            {
                _log.LogWarning("CreateProcessAsUser failed ({Path}): win32 error {Err}", _uiExePath, Marshal.GetLastWin32Error());
            }
        }
        finally
        {
            Win32.CloseHandleSafe(token);
        }
    }
}
