using System.Windows;
using MiniPlayer.Services;

namespace MiniPlayer;

public partial class App : Application
{
    private const string SingleInstanceName = @"Local\WindowsMiniPlayer_2f7d1c60";

    private Mutex? _singleInstance;
    private bool _ownsMutex;
    private MainWindow? _window;
    private TrayService? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstance = new Mutex(initiallyOwned: true, SingleInstanceName, out _ownsMutex);
        if (!_ownsMutex)
        {
            // Another copy is already running; bow out without touching the mutex.
            Shutdown();
            return;
        }

        base.OnStartup(e);

        _window = new MainWindow();
        _tray = new TrayService(_window);
        _window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _window?.Dispose();

        if (_ownsMutex)
        {
            _singleInstance?.ReleaseMutex();
        }
        _singleInstance?.Dispose();

        base.OnExit(e);
    }
}
