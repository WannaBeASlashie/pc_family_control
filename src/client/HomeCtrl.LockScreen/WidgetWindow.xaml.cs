using System.Windows;
using System.Windows.Threading;

namespace HomeCtrl.LockScreen;

/// <summary>解锁后的常驻小组件：右上角显示剩余时间 + 下机按钮。</summary>
public partial class WidgetWindow : Window
{
    private readonly PipeClient _pipe;
    private long _balance;
    private int? _lastSessionId;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };

    public WidgetWindow(PipeClient pipe)
    {
        InitializeComponent();
        _pipe = pipe;

        Left = SystemParameters.WorkArea.Width - Width - 12;
        Top = 12;
        Closing += (_, e) => e.Cancel = true; // 禁止关闭

        _timer.Tick += (_, _) =>
        {
            if (_balance > 0) _balance--; // 本地走秒，心跳每 10s 校准
            UpdateTime();
        };
        _timer.Start();
    }

    public void ApplyState(StateSnapshot st)
    {
        // 会话切换时清空上一条家长消息
        if (_lastSessionId != st.SessionId)
        {
            MsgText.Text = st.Message ?? "";
            _lastSessionId = st.SessionId;
        }
        else if (st.Message is not null)
        {
            MsgText.Text = st.Message;
        }

        _balance = st.BalanceSeconds;
        NameText.Text = st.PersonName ?? "—";
        UpdateTime();
    }

    private void UpdateTime()
    {
        var t = TimeSpan.FromSeconds(Math.Max(_balance, 0));
        TimeText.Text = $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}";
    }

    private async void OnLogout(object sender, RoutedEventArgs e)
    {
        LogoutBtn.IsEnabled = false;
        try
        {
            await _pipe.LogoutAsync();
        }
        finally
        {
            LogoutBtn.IsEnabled = true;
        }
    }
}
