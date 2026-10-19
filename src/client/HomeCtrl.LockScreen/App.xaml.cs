using System.Windows;

namespace HomeCtrl.LockScreen;

public partial class App : Application
{
    private Mutex? _instanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        // 单实例：防止双开叠窗、抢占管道
        _instanceMutex = new Mutex(true, @"Global\HomeCtrl.LockScreen.SingleInstance", out var createdNew);
        if (!createdNew)
        {
            Shutdown();
            return;
        }
        base.OnStartup(e);
    }
}
