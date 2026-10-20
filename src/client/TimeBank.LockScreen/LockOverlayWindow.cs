using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WillBeyond.TimeBank.LockScreen;

/// <summary>
/// 副显示器的静态锁屏遮罩：与主窗口同底色/品牌，覆盖该屏幕全幅（含任务栏），
/// 防止从第二块屏幕绕过锁屏。不可交互、不可关闭、不抢焦点。
/// </summary>
internal sealed class LockOverlayWindow : Window
{
    public LockOverlayWindow(System.Drawing.Rectangle bounds)
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Background = Brush(0x0E, 0x14, 0x20);
        Left = bounds.Left;
        Top = bounds.Top;
        Width = bounds.Width;
        Height = bounds.Height;

        var title = new TextBlock
        {
            Text = "TimeBank",
            FontSize = 42,
            FontWeight = FontWeights.Bold,
            Foreground = Brush(0xE8, 0xEC, 0xF4),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        var brand = new TextBlock
        {
            Text = "willBeyond",
            FontSize = 13,
            Foreground = Brush(0x5A, 0x64, 0x78),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 0),
        };
        var hint = new TextBlock
        {
            Text = "Locked — sign in on the main display",
            FontSize = 15,
            Foreground = Brush(0x8A, 0x93, 0xA8),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 28, 0, 0),
        };

        var panel = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        panel.Children.Add(title);
        panel.Children.Add(brand);
        panel.Children.Add(hint);
        Content = panel;

        Closing += (_, e) => e.Cancel = true; // 生产模式不可关闭（Alt+F4 由钩子拦截）
    }

    private static SolidColorBrush Brush(byte r, byte g, byte b) =>
        new(Color.FromRgb(r, g, b));
}
