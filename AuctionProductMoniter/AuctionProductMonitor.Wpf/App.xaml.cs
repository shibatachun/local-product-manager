namespace AuctionProductMonitor.Wpf;

public partial class App : System.Windows.Application
{
    private const string SingleInstanceMutexName = @"Global\AuctionProductMonitor.Wpf.SingleInstance";
    private System.Threading.Mutex? _singleInstanceMutex;

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        LanguageManager.Initialize();
        _singleInstanceMutex = new System.Threading.Mutex(true, SingleInstanceMutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            System.Windows.MessageBox.Show(
                LanguageManager.Get("AnotherInstanceRunning"),
                LanguageManager.Get("WindowTitle"),
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            Shutdown();
            return;
        }
        base.OnStartup(e);
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        _singleInstanceMutex = null;
        base.OnExit(e);
    }
}
