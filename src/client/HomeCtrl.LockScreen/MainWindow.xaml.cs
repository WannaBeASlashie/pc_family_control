using System.Windows;
using System.Windows.Input;

namespace HomeCtrl.LockScreen;

/// <summary>全屏锁屏窗口：无标题栏、置顶、禁关闭。</summary>
public partial class MainWindow : Window
{
    private readonly PipeClient _pipe = new();
    private WidgetWindow? _widget;

    public MainWindow()
    {
        InitializeComponent();
        Closing += (_, e) => e.Cancel = true; // 禁止 Alt+F4 关闭
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
            FooterText.Text = st.ServerOnline ? "" : "⚠ 无法连接服务器，请稍候或联系家长";

            if (st.SessionId is null || st.Locked)
            {
                Show();
                Activate();
                _widget?.Hide();
                CardBox.Focus();
                Keyboard.Focus(CardBox);
            }
            else
            {
                Hide();
                _widget ??= new WidgetWindow(_pipe);
                _widget.Show();
                _widget.ApplyState(st);
            }
        });
    }

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

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _ = LoginAsync();
            e.Handled = true;
        }
    }
}
