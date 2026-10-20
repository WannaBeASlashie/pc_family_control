using System.Runtime.InteropServices;

namespace WillBeyond.TimeBank.Agent;

internal static class Win32
{
    public const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;

    [DllImport("kernel32.dll")]
    private static extern int WTSGetActiveConsoleSessionId();

    [DllImport("wtsapi32.dll")]
    private static extern bool WTSLogoffSession(IntPtr hServer, int sessionId, bool bWait);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSQueryUserToken(int sessionId, out IntPtr phToken);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessAsUser(
        IntPtr hToken,
        string? lpApplicationName,
        string? lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        ref STARTUPINFO lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    /// <summary>当前活动控制台会话 ID（无用户登录时为 -1）。</summary>
    public static int GetActiveConsoleSessionId() => WTSGetActiveConsoleSessionId();

    /// <summary>注销当前活动控制台会话（SYSTEM 服务可调用，用于强制下机）。</summary>
    public static void LogoffActiveSession()
    {
        var sessionId = WTSGetActiveConsoleSessionId();
        if (sessionId != -1)
            WTSLogoffSession(IntPtr.Zero, sessionId, false);
    }

    /// <summary>查询指定会话的登录用户主 token（调用方需 SE_TCB_NAME，LocalSystem 具备）。</summary>
    public static bool TryGetUserToken(int sessionId, out IntPtr token) =>
        WTSQueryUserToken(sessionId, out token);

    public static bool TryCreateProcessAsUser(
        IntPtr token,
        string commandLine,
        string? workingDirectory,
        string? desktop,
        out PROCESS_INFORMATION pi)
    {
        var si = new STARTUPINFO
        {
            cb = Marshal.SizeOf<STARTUPINFO>(),
            lpDesktop = desktop,
        };
        pi = default;
        return CreateProcessAsUser(
            token,
            null,
            commandLine,
            IntPtr.Zero,
            IntPtr.Zero,
            false,
            CREATE_UNICODE_ENVIRONMENT,
            IntPtr.Zero,
            workingDirectory,
            ref si,
            out pi);
    }

    public static void CloseHandleSafe(IntPtr handle)
    {
        if (handle != IntPtr.Zero) CloseHandle(handle);
    }
}
