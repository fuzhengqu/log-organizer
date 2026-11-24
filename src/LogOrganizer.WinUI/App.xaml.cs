using Microsoft.UI.Xaml;

namespace LogOrganizer.WinUI;

public partial class App : Application
{
    private Mutex? _mutex;
    private TrayApplicationContext? _application;

    public App()
    {
        InitializeComponent();
    }

    internal void ToggleWindow() => _application?.ToggleWindow();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            _mutex = new Mutex(initiallyOwned: false, TrayApplicationContext.MutexName, out _);
            if (!_mutex.WaitOne(TimeSpan.Zero, exitContext: false))
            {
                _mutex.Dispose();
                _mutex = null;
                TrayApplicationContext.ShowAlreadyRunning();
                Exit();
                return;
            }
        }
        catch (AbandonedMutexException)
        {
            // An abandoned mutex is now owned by this process.
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex, "单实例检查失败，按首次启动继续");
        }

        try
        {
            _application = new TrayApplicationContext(this);
            _application.Start();
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex, "启动失败");
            TrayApplicationContext.ShowStartupError(ex);
            Exit();
        }
    }

    internal void Shutdown()
    {
        _application?.Dispose();
        _application = null;
        ReleaseSingleInstance();
        Exit();
    }

    internal void ReleaseSingleInstance()
    {
        try
        {
            _mutex?.ReleaseMutex();
        }
        catch (ApplicationException)
        {
        }

        _mutex?.Dispose();
        _mutex = null;
    }
}
