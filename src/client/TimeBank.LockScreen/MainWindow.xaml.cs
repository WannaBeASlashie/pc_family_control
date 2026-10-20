using System.Windows;
using System.Windows.Input;

namespace WillBeyond.TimeBank.LockScreen;

/// <summary>
/// 全屏锁屏窗口：无标题栏、置顶、禁关闭（生产模式）。
/// 加固：覆盖主显示器全幅（含任务栏）；其余显示器各铺一块静态遮罩；
/// 生产模式下安装低级键盘钩子拦截 Win/Alt+Tab/Alt+F4/Ctrl+Esc。
/// </summary>
public partial class MainWindow : Window
{
    private readonly bool _testMode;   // HOMECTRL_TEST=1 时为调试模式：允许 ESC 退出
    private readonly PipeClient _pipe = new();
    private WidgetWindow? _widget;
    private readonly List<LockOverlayWindow> _overlays = new();
    private LockGuard? _guard;

    public MainWindow()
    {
        InitializeComponent();
        _testMode = Environment.GetEnvironmentVariable("HOMECTRL_TEST") == "1";
        if (_testMode) TestHint.Visibility = Visibility.Visible;
        Closing += (_, e) => e.Cancel = !_testMode; // 生产模式禁止 Alt+F4 关闭

        PositionToPrimaryScreen();

        if (!_testMode)
        {
            foreach (var screen in System.Windows.Forms.Screen.AllScreens)
            {
                if (screen.Primary) continue;
                _overlays.Add(new LockOverlayWindow(screen.Bounds));
            }
            _guard = new LockGuard();
        }

        IsVisibleChanged += OnIsVisibleChanged;
        Loaded += async (_, _) => await StartAsync();
    }

    /// <summary>锁屏铺满主显示器物理分辨率（含任务栏区域），避免 Maximized 只铺工作区。</summary>
    private void PositionToPrimaryScreen()
    {
        var b = System.Windows.Forms.Screen.PrimaryScreen?.Bounds
            ?? new System.Drawing.Rectangle(0, 0,
                (int)SystemParameters.PrimaryScreenWidth,
                (int)SystemParameters.PrimaryScreenHeight);
        WindowState = WindowState.Normal;
        Left = b.Left;
        Top = b.Top;
        Width = b.Width;
        Height = b.Height;
    }

    /// <summary>锁屏可见 ⇔ 键盘钩子激活 + 副屏遮罩显示；解锁后全部放行/隐藏。</summary>
    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        var visible = (bool)e.NewValue;
        if (_guard is not null) _guard.Active = visible;
        foreach (var overlay in _overlays)
        {
            if (visible) overlay.Show();
            else overlay.Hide();
        }
    }

    private async Task StartAsync()
    {
        _pipe.OnState += ApplyState;
        _ = _pipe.ConnectLoopAsync();

        if (!await _pipe.WaitConnectedAsync(TimeSpan.FromSeconds(30)))
        {
            FooterText.Text = "Local agent is not running";
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
                ? (st.Offline ? "Offline mode (limited)" : "Cannot reach the server, retrying...")
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
            StatusText.Text = "Card number and PIN are required";
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
                StatusText.Text = reason ?? "Sign-in failed";
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
                ? $"Request sent: +{minutes} min, waiting for approval..."
                : reason ?? "Request failed, try again later";
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
