using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WillBeyond.TimeBank.LockScreen;

/// <summary>
/// 低级键盘钩子（WH_KEYBOARD_LL）：锁屏激活时屏蔽 Win 键、Alt+Tab、Alt+F4、
/// Alt+Space、Ctrl+Esc / Ctrl+Shift+Esc，堵住应用层锁屏最常见的绕过方式。
///
/// 说明：
///  - 仅生产模式安装（测试模式不装，方便 ESC 调试）。
///  - <see cref="Active"/> 仅在锁屏可见时为 true，解锁（小组件态）后放行所有按键。
///  - Ctrl+Alt+Del（安全注意力序列）由 Winlogon 处理，无法在用户态拦截，属 P3 安全桌面范畴。
///  - 钩子回调运行在安装线程（WPF UI 线程）的消息循环里，必须快速返回且绝不抛异常。
/// </summary>
internal sealed class LockGuard : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;

    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;

    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;
    private const int VK_TAB = 0x09;
    private const int VK_F4 = 0x73;
    private const int VK_SPACE = 0x20;
    private const int VK_ESCAPE = 0x1B;
    private const int VK_MENU = 0x12;
    private const int VK_CONTROL = 0x11;

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    private readonly LowLevelKeyboardProc _proc;
    private readonly IntPtr _hook;
    private volatile bool _active = true;

    /// <summary>锁屏是否处于激活态（可见）。false 时放行全部按键。</summary>
    public bool Active
    {
        get => _active;
        set => _active = value;
    }

    public LockGuard()
    {
        _proc = HookCallback;
        using var proc = Process.GetCurrentProcess();
        using var mod = proc.MainModule!;
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(mod.ModuleName), 0);
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (nCode >= 0 && _active)
            {
                var msg = (int)wParam;
                var vk = Marshal.ReadInt32(lParam); // KBDLLHOOKSTRUCT.vkCode（结构体首个字段）
                var alt = IsDown(VK_MENU);
                var ctrl = IsDown(VK_CONTROL);

                // Win 键在 keyup 时才弹开始菜单，因此 down/up 都要拦
                if (vk == VK_LWIN || vk == VK_RWIN)
                {
                    if (msg is WM_KEYDOWN or WM_KEYUP or WM_SYSKEYDOWN or WM_SYSKEYUP)
                        return (IntPtr)1;
                }

                if (msg is WM_KEYDOWN or WM_SYSKEYDOWN)
                {
                    // Alt+Tab / Alt+F4 / Alt+Esc / Alt+Space（窗口菜单）
                    if (alt && (vk == VK_TAB || vk == VK_F4 || vk == VK_ESCAPE || vk == VK_SPACE))
                        return (IntPtr)1;
                    // Ctrl+Esc（开始菜单）/ Ctrl+Shift+Esc（任务管理器）
                    if (ctrl && vk == VK_ESCAPE)
                        return (IntPtr)1;
                }
            }
        }
        catch
        {
            // 钩子回调绝不能把异常抛回系统
        }

        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private static bool IsDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    public void Dispose() => UnhookWindowsHookEx(_hook);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
}
