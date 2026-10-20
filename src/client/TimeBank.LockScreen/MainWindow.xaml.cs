using System.Windows;
using System.Windows.Input;

namespace WillBeyond.TimeBank.LockScreen;

/// <summary>全屏锁屏窗口：无标题栏、置顶、禁关闭（生产模式）。</summary>
public partial class MainWindow : Window
{
    private readonly bool _testMode;   // HOMECTRL_TEST=1 时为调试模式：允许 ESC 退出
    private readonly PipeClient _pipe = new();
    private WidgetWindow? _widget;

    public MainWindow()
    {
        InitializeComponent();
        _testMode = Environment.GetEnvironmentVariable("HOMECTRL_TEST") == "1";
        if (_testMode) TestHint.Visibility = Visibility.Visible;
        Closing += (_, e) => e.Cancel = !_testMode; // 生产模式禁止 Alt+F4 关闭
        Loaded += async (_, _) => await StartAsync();
    }

    private async Task StartAsync()
    {
        _pipe.OnState += ApplyState;
        _ = _pipe.ConnectLoopAsync();

        if (!await _pipe.WaitConnectedAsync(TimeSpan.FromSeconds(30)))
        {
            FooterText.Text = "⚠ 本机管控服务未运行，请联系家长";
            return;
        }

        var st = await _pipe.GetStateAsync();
        if (st is not null)
            ApplyState(st);
    }

    private void ApplyState(StateSnapshot st)
    {
        Dispatcher.Invoke(() =>
        {
            FooterText.Text = !st.ServerOnline
                ? (st.Offline ? "离线模式（受限上机）" : "⚠ 无法连接服务器，请稍候或联系家长")
                : "";

            // 离线模式不可申请加时
            Req15Btn.IsEnabled = !st.Offline;
            Req30Btn.IsEnabled = !st.Offline;
            OfflineReqHint.Visibility = st.Offline ? Visibility.Visible : Visibility.Collapsed;

            if (st.SessionId is null)
            {
                // 无会话：显示登录面板
                ExhaustedPanel.Visibility = Visibility.Collapsed;
                LoginPanel.Visibility = Visibility.Visible;
                Show();
                Activate();
                _widget?.Hide();
                CardBox.Focus();
                Keyboard.Focus(CardBox);
            }
            else if (st.Locked)
            {
                // 会话还在但被锁（余额耗尽）：显示申请加时面板
                ExhaustedName.Text = st.PersonName ?? "—";
                LoginPanel.Visibility = Visibility.Collapsed;
                ExhaustedPanel.Visibility = Visibility.Visible;
                Show();
                Activate();
                _widget?.Hide();
            }
            else
            {
                // 已解锁：显示剩余时间小组件
                Hide();
                _widget ??= new WidgetWindow(_pipe, _testMode);
                _widget.Show();
                _widget.ApplyState(st);
            }
        });
    }

    // ---------- 登录 ----------

    private async void OnLogin(object sender, RoutedEventArgs e) => await LoginAsync();

    private async Task LoginAsync()
    {
        var cardNo = CardBox.Text.Trim();
        var pin = PinBox.Password;
        if (cardNo.Length == 0 || pin.Length == 0)
        {
            StatusText.Text = "请输入卡号和 PIN";
            return;
        }

        LoginBtn.IsEnabled = false;
        try
        {
            var (ok, reason) = await _pipe.LoginAsync(cardNo, pin);
            if (ok)
            {
                PinBox.Clear();
                StatusText.Text = "";
            }
            else
            {
                StatusText.Text = reason ?? "上机失败";
            }
        }
        finally
        {
            LoginBtn.IsEnabled = true;
        }
    }

    // ---------- 申请加时（FR-11a）----------

    private async void OnRequest15(object sender, RoutedEventArgs e) => await RequestTimeAsync(15);
    private async void OnRequest30(object sender, RoutedEventArgs e) => await RequestTimeAsync(30);

    private async Task RequestTimeAsync(int minutes)
    {
        Req15Btn.IsEnabled = false;
        Req30Btn.IsEnabled = false;
        StatusText.Text = "";
        try
        {
            var (ok, reason) = await _pipe.RequestTimeAsync(minutes);
            StatusText.Text = ok
                ? $"已发送 +{minutes} 分钟申请，等待家长批准…"
                : reason ?? "申请失败，请稍后再试";
        }
        finally
        {
            Req15Btn.IsEnabled = true;
            Req30Btn.IsEnabled = true;
        }
    }

    private async void OnGiveUp(object sender, RoutedEventArgs e)
    {
        GiveUpBtn.IsEnabled = false;
        try
        {
            await _pipe.LogoutAsync(); // 主动结束会话（停表）
        }
        finally
        {
            GiveUpBtn.IsEnabled = true;
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_testMode && e.Key == Key.Escape)
        {
            Application.Current.Shutdown();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Enter)
        {
            _ = LoginAsync();
            e.Handled = true;
        }
    }
}
