using System.Windows;

namespace WillBeyond.TimeBank.LockScreen;

public partial class App : Application
{
    private Mutex? _instanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        // 单实例：防止双开叠窗、抢占管道
        _instanceMutex = new Mutex(true, @"Global\WillBeyond.TimeBank.LockScreen.SingleInstance", out var createdNew);
        if (!createdNew)
        {
            Shutdown();
            return;
        }
        base.OnStartup(e);
    }
}
