using System.Runtime.InteropServices;

namespace WillBeyond.TimeBank.Agent;

internal static class Win32
{
    [DllImport("kernel32.dll")]
    private static extern int WTSGetActiveConsoleSessionId();

    [DllImport("wtsapi32.dll")]
    private static extern bool WTSLogoffSession(IntPtr hServer, int sessionId, bool bWait);

    /// <summary>注销当前活动控制台会话（SYSTEM 服务可调用，用于强制下机）。</summary>
    public static void LogoffActiveSession()
    {
        var sessionId = WTSGetActiveConsoleSessionId();
        if (sessionId != -1)
            WTSLogoffSession(IntPtr.Zero, sessionId, false);
    }
}
