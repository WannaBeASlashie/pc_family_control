using System.Windows;
using System.Windows.Threading;

namespace HomeCtrl.LockScreen;

/// <summary>顶部居中的自动消失提醒（FR-07a：10/5/1 分钟提醒）。不抢焦点。</summary>
public partial class ToastWindow : Window
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(8) };

    public ToastWindow(string text)
    {
        InitializeComponent();
        ToastText.Text = text;

        var area = SystemParameters.WorkArea;
        Left = (area.Width - Width) / 2;
        Top = 36;

        _timer.Tick += (_, _) => Close();
        _timer.Start();
    }
}
