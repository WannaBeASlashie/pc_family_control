using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace HomeCtrl.LockScreen;

/// <summary>解锁后的常驻小组件：右上角显示剩余时间 + 下机按钮。</summary>
public partial class WidgetWindow : Window
{
    private readonly PipeClient _pipe;
    private readonly bool _testMode;
    private long _balance;
    private long _lastBalance = long.MaxValue;     // 上次提醒判定用的余额
    private int? _lastSessionId;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };

    // FR-07a：剩余 10/5/1 分钟时提醒（秒）
    private static readonly long[] ReminderThresholds = { 600, 300, 60 };

    public WidgetWindow(PipeClient pipe, bool testMode = false)
    {
        InitializeComponent();
        _pipe = pipe;
        _testMode = testMode;

        Left = SystemParameters.WorkArea.Width - Width - 12;
        Top = 12;
        Closing += (_, e) => e.Cancel = !_testMode; // 生产模式禁止关闭
        if (_testMode)
            PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape) { Application.Current.Shutdown(); e.Handled = true; }
            };

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
            _lastBalance = long.MaxValue;          // 新会话重新开始阈值判定
        }
        else if (st.Message is not null)
        {
            MsgText.Text = st.Message;
        }

        CheckReminders(st.BalanceSeconds);
        _balance = st.BalanceSeconds;
        NameText.Text = st.PersonName ?? "—";
        UpdateTime();
    }

    /// <summary>跨越提醒阈值（600/300/60s）时弹 Toast；一次跳过多个阈值只提醒最高的。</summary>
    private void CheckReminders(long newBalance)
    {
        if (newBalance <= 0 || _lastBalance <= 0)
        {
            _lastBalance = newBalance;
            return;
        }

        foreach (var t in ReminderThresholds)
        {
            if (_lastBalance >= t && newBalance < t)
            {
                var minutes = t / 60;
                new ToastWindow($"⏰ 剩余时间不足 {minutes} 分钟，请注意保存").Show();
                break;
            }
        }
        _lastBalance = newBalance;
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
