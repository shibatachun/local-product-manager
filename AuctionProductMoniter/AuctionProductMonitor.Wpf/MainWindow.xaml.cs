using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AuctionProductMonitor.Wpf;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<SaleEvent> _currentSales = [];
    private readonly ObservableCollection<LiveSession> _completedSessions = [];
    private readonly HashSet<string> _receivedSaleKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<LiveSession> _sessions;
    private readonly LocalReceiver _receiver = new();
    private readonly RemotePrintClient _remotePrinter = new();
    private readonly MonitorDataStore _store = new();
    private readonly RemotePrinterSettings _printerSettings;
    private LiveSession? _activeSession;
    private LiveSession? _viewedHistoricalSession;
    private static string ScreenshotRoot => Path.Combine(AppContext.BaseDirectory, "Screenshots");

    public MainWindow()
    {
        _printerSettings = RemotePrinterSettings.Load();
        InitializeComponent();
        RelayEndpointTextBox.Text = _printerSettings.Endpoint;
        RelayApiKeyBox.Password = _printerSettings.ApiKey;
        LanguageComboBox.SelectedIndex = LanguageManager.CurrentLanguage == LanguageManager.English ? 1 : 0;
        _sessions = _store.Load();
        SalesGrid.ItemsSource = _currentSales;
        SessionsGrid.ItemsSource = _completedSessions;
        RestoreSessions();
        _receiver.SaleReceived += OnSaleReceived;
        _receiver.ScreenshotReceived = SaveScreenshotAsync;
        Loaded += OnLoaded;
        Closing += OnClosing;
        Closed += OnClosed;
    }

    private void RestoreSessions()
    {
        foreach (var session in _sessions.Where(item => item.EndedAt is not null).OrderByDescending(item => item.StartedAt))
            _completedSessions.Add(session);

        _activeSession = _sessions.Where(item => item.EndedAt is null).OrderByDescending(item => item.StartedAt).FirstOrDefault();
        if (_activeSession is not null)
        {
            foreach (var sale in _activeSession.Sales.OrderByDescending(item => item.DetectedAt))
            {
                _currentSales.Add(sale);
                _receivedSaleKeys.Add(SaleKey(sale));
            }
        }
        UpdateSessionDisplay();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await _receiver.StartAsync();
            StatusText.Text = LanguageManager.Get("ReceiverRunning", "http://127.0.0.1:17891", _store.FilePath);
        }
        catch (Exception ex)
        {
            StatusDot.Fill = Brushes.IndianRed;
            StatusText.Text = LanguageManager.Get("StartFailed", ex.Message);
        }
    }

    private void ToggleLiveButton_Click(object sender, RoutedEventArgs e)
    {
        if (_activeSession is null) StartLive();
        else EndLive();
    }

    private void StartLive()
    {
        var startedAt = DateTimeOffset.Now;
        var folder = Path.Combine(ScreenshotRoot, startedAt.ToLocalTime().ToString("yyyy-MM-dd_HH-mm-ss"));
        Directory.CreateDirectory(folder);
        _activeSession = new LiveSession { Id = $"LIVE-{startedAt:yyyyMMdd-HHmmss}", StartedAt = startedAt, ScreenshotFolder = folder };
        _sessions.Add(_activeSession);
        _currentSales.Clear();
        _viewedHistoricalSession = null;
        SessionsGrid.SelectedItem = null;
        BackToCurrentSessionButton.Visibility = Visibility.Collapsed;
        _receivedSaleKeys.Clear();
        SaveSessions();
        UpdateSessionDisplay();
    }

    private void EndLive()
    {
        if (_activeSession is null) return;
        _activeSession.EndedAt = DateTimeOffset.Now;
        _completedSessions.Insert(0, _activeSession);
        if (_viewedHistoricalSession is null)
            CurrentSalesTitle.Text = LanguageManager.Get("LastEndedSession", _activeSession.Id, _activeSession.SaleCount);
        _activeSession = null;
        SaveSessions();
        UpdateSessionDisplay(keepCurrentTitle: true);
    }

    private void SessionsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SessionsGrid.SelectedItem is not LiveSession session) return;
        _viewedHistoricalSession = session;
        _currentSales.Clear();
        foreach (var sale in session.Sales.OrderByDescending(item => item.DetectedAt))
            _currentSales.Add(sale);
        CurrentSalesTitle.Text = LanguageManager.Get("HistoricalSessionSales", session.Id, session.SaleCount);
        BackToCurrentSessionButton.Visibility = Visibility.Visible;
    }

    private void BackToCurrentSessionButton_Click(object sender, RoutedEventArgs e)
    {
        ShowCurrentSessionSales();
    }

    private void ShowCurrentSessionSales()
    {
        _viewedHistoricalSession = null;
        SessionsGrid.SelectedItem = null;
        _currentSales.Clear();
        if (_activeSession is not null)
            foreach (var sale in _activeSession.Sales.OrderByDescending(item => item.DetectedAt))
                _currentSales.Add(sale);
        BackToCurrentSessionButton.Visibility = Visibility.Collapsed;
        UpdateSessionDisplay();
    }

    private void UpdateSessionDisplay(bool keepCurrentTitle = false)
    {
        if (_activeSession is null)
        {
            ToggleLiveButton.Content = LanguageManager.Get("StartLive");
            ToggleLiveButton.Background = new SolidColorBrush(Color.FromRgb(36, 87, 214));
            SessionStatusText.Text = LanguageManager.Get("NoActiveSession");
            if (!keepCurrentTitle) CurrentSalesTitle.Text = LanguageManager.Get("CurrentSalesNotStarted");
            return;
        }

        ToggleLiveButton.Content = LanguageManager.Get("EndLive");
        ToggleLiveButton.Background = new SolidColorBrush(Color.FromRgb(190, 45, 45));
        SessionStatusText.Text = LanguageManager.Get("ActiveSession", _activeSession.Id, _activeSession.StartedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
        CurrentSalesTitle.Text = LanguageManager.Get("CurrentSales", _activeSession.Id);
    }

    private void OnSaleReceived(SaleEvent sale)
    {
        Dispatcher.Invoke(() =>
        {
            if (_activeSession is null)
            {
                SessionStatusText.Text = LanguageManager.Get("IgnoredSale", sale.ProductNumber);
                return;
            }

            sale.SessionId = _activeSession.Id;
            if (!_receivedSaleKeys.Add(SaleKey(sale))) return;
            _activeSession.Sales.Add(sale);
            if (_viewedHistoricalSession is null)
                _currentSales.Insert(0, sale);
            SaveSessions();
            CurrentSalesTitle.Text = LanguageManager.Get("CurrentSalesCount", _activeSession.Id, _activeSession.SaleCount);
            if (AutoPrintCheckBox.IsChecked == true) _ = PrintSaleAsync(sale);
        });
    }

    private static string SaleKey(SaleEvent sale) => $"{sale.BuyerName.Trim()}|{sale.ProductNumber.Trim()}";

    private async Task<bool> SaveScreenshotAsync(ScreenshotCapture capture)
    {
        var session = await Dispatcher.InvokeAsync(() => _sessions
            .OrderByDescending(item => item.StartedAt)
            .FirstOrDefault(item => item.Sales.Any(sale =>
                sale.BuyerName.Equals(capture.BuyerName, StringComparison.OrdinalIgnoreCase) &&
                sale.ProductNumber.Equals(capture.ProductNumber, StringComparison.OrdinalIgnoreCase) &&
                Math.Abs((sale.DetectedAt - capture.CapturedAt).TotalSeconds) < 30)));
        if (session is null) return false;

        var expectedFolder = Path.Combine(ScreenshotRoot, session.StartedAt.ToLocalTime().ToString("yyyy-MM-dd_HH-mm-ss"));
        if (string.IsNullOrWhiteSpace(session.ScreenshotFolder) ||
            !Path.GetFullPath(session.ScreenshotFolder).StartsWith(Path.GetFullPath(ScreenshotRoot), StringComparison.OrdinalIgnoreCase))
            session.ScreenshotFolder = expectedFolder;
        Directory.CreateDirectory(session.ScreenshotFolder);

        var comma = capture.ImageDataUrl.IndexOf(',');
        if (comma < 0) return false;
        byte[] bytes;
        try { bytes = Convert.FromBase64String(capture.ImageDataUrl[(comma + 1)..]); }
        catch (FormatException) { return false; }

        var baseName = $"{SafeFilePart(capture.BuyerName)}_{SafeFilePart(capture.ProductNumber)}";
        var path = Path.Combine(session.ScreenshotFolder, baseName + ".jpg");
        for (var suffix = 2; File.Exists(path); suffix++)
            path = Path.Combine(session.ScreenshotFolder, $"{baseName}_{suffix}.jpg");
        await File.WriteAllBytesAsync(path, bytes);
        await Dispatcher.InvokeAsync(() => StatusText.Text = $"Screenshot saved: {path}");
        return true;
    }

    private static string SafeFilePart(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var safe = new string(value.Trim().Select(character => invalid.Contains(character) ? '_' : character).ToArray()).Trim('.', ' ');
        return string.IsNullOrWhiteSpace(safe) ? "unknown" : safe;
    }

    private void OpenScreenshotFolderButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(ScreenshotRoot);
            Process.Start(new ProcessStartInfo
            {
                FileName = ScreenshotRoot,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            StatusDot.Fill = Brushes.IndianRed;
            StatusText.Text = LanguageManager.Get("OpenScreenshotFolderFailed", ex.Message);
        }
    }

    private void SaveSessions()
    {
        try
        {
            _store.Save(_sessions);
        }
        catch (Exception ex)
        {
            StatusDot.Fill = Brushes.IndianRed;
            StatusText.Text = LanguageManager.Get("SaveFailed", ex.Message);
        }
    }

    private async void TestRelayButton_Click(object sender, RoutedEventArgs e)
        => await SendToRelayAsync(null, true);

    private async void PrintTestLabelButton_Click(object sender, RoutedEventArgs e)
    {
        var sample = new SaleEvent
        {
            BuyerName = "SAMPLE BUYER",
            ProductNumber = "TEST-001",
            SessionId = "PRINT-TEST",
            DetectedAt = DateTimeOffset.Now,
            SourceText = "Auction Product Monitor sample label"
        };
        PrintStatusText.Text = LanguageManager.Get("PrintingTestLabel");
        try
        {
            await _remotePrinter.SendAsync(RelayEndpointTextBox.Text, RelayApiKeyBox.Password, sample, false);
            PrintStatusText.Text = LanguageManager.Get("TestLabelAccepted");
        }
        catch (Exception ex)
        {
            PrintStatusText.Text = LanguageManager.Get("PrintFailed", ex.Message);
        }
    }

    private async void PrintSelectedButton_Click(object sender, RoutedEventArgs e)
    {
        if (SalesGrid.SelectedItem is not SaleEvent sale)
        {
            PrintStatusText.Text = LanguageManager.Get("SelectSaleFirst");
            return;
        }
        await PrintSaleAsync(sale);
    }

    private Task PrintSaleAsync(SaleEvent sale) => SendToRelayAsync(sale, false);

    private async Task SendToRelayAsync(SaleEvent? sale, bool test)
    {
        try
        {
            PrintStatusText.Text = test ? LanguageManager.Get("TestingConnection") : LanguageManager.Get("PrintingProduct", sale!.ProductNumber);
            await _remotePrinter.SendAsync(RelayEndpointTextBox.Text, RelayApiKeyBox.Password, sale, test);
            PrintStatusText.Text = test ? LanguageManager.Get("ConnectionSuccess") : LanguageManager.Get("PrintAccepted", sale!.ProductNumber);
        }
        catch (Exception ex)
        {
            PrintStatusText.Text = LanguageManager.Get("PrintFailed", ex.Message);
        }
    }

    private void SaveRelaySettingsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _printerSettings.Endpoint = RelayEndpointTextBox.Text.Trim();
            _printerSettings.ApiKey = RelayApiKeyBox.Password.Trim();
            _printerSettings.Save();
            PrintStatusText.Text = LanguageManager.Get("SettingsSaved");
        }
        catch (Exception ex)
        {
            PrintStatusText.Text = LanguageManager.Get("SaveFailed", ex.Message);
        }
    }

    private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LanguageComboBox.SelectedItem is not ComboBoxItem item || item.Tag is not string language)
            return;

        LanguageManager.ChangeLanguage(language);
        if (_viewedHistoricalSession is not null)
            CurrentSalesTitle.Text = LanguageManager.Get("HistoricalSessionSales", _viewedHistoricalSession.Id, _viewedHistoricalSession.SaleCount);
        else
            UpdateSessionDisplay();
        PrintStatusText.Text = LanguageManager.Get("PrintNotTested");
        SessionsGrid.Items.Refresh();
    }

    private async void OnClosed(object? sender, EventArgs e)
    {
        SaveSessions();
        _receiver.SaleReceived -= OnSaleReceived;
        _receiver.ScreenshotReceived = null;
        await _receiver.DisposeAsync();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_activeSession is null) return;

        var choice = MessageBox.Show(
            LanguageManager.Get("CloseWhileLivePrompt", _activeSession.Id),
            LanguageManager.Get("CloseWhileLiveTitle"),
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning,
            MessageBoxResult.Yes);
        if (choice == MessageBoxResult.Cancel)
        {
            e.Cancel = true;
            return;
        }
        if (choice == MessageBoxResult.Yes) EndLive();
    }
}
