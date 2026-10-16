using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

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
    private bool _printingAll;
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
        foreach (var sale in OrderedSales(session))
            _currentSales.Add(sale);
        CurrentSalesTitle.Text = LanguageManager.Get("HistoricalSessionSales", session.Id, session.SaleCount);
        BackToCurrentSessionButton.Visibility = Visibility.Visible;
    }

    private void BackToCurrentSessionButton_Click(object sender, RoutedEventArgs e)
    {
        ShowCurrentSessionSales();
    }

    private void ImportShowReportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_activeSession is not null)
        {
            MessageBox.Show(LanguageManager.Get("EndLiveBeforeImport"), LanguageManager.Get("ImportShowReport"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var dialog = new OpenFileDialog
        {
            Title = LanguageManager.Get("ImportShowReport"),
            Filter = "Whatnot show report (*.csv)|*.csv|All files (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var report = WhatnotShowReportImporter.Read(dialog.FileName);
            var existing = _sessions.FirstOrDefault(session => session.ShowReportSha256 == report.Sha256);
            if (existing is not null)
            {
                ShowSession(existing);
                StatusText.Text = LanguageManager.Get("ShowAlreadyImported", existing.Id);
                return;
            }
            var importedIds = _sessions.Where(session => session.Source == "show-report")
                .SelectMany(session => session.Sales).Select(sale => sale.OrderId)
                .Where(id => !string.IsNullOrWhiteSpace(id)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var repeated = report.Sales.Where(sale => importedIds.Contains(sale.OrderId)).ToArray();
            if (repeated.Length > 0)
                throw new InvalidDataException(LanguageManager.Get("ShowOrderOverlap", repeated.Length));

            var labelCount = report.Sales.Sum(sale => sale.Quantity);
            var confirmation = LanguageManager.Get("ConfirmShowImport", report.ShowDate, report.Sales.Count, labelCount, report.IgnoredRows);
            if (report.UnnumberedOrders.Count > 0)
            {
                var unnumberedList = string.Join(Environment.NewLine, report.UnnumberedOrders.Take(10)
                    .Select(row => $"CSV row {row.RowNumber}: {row.Title} ({row.Buyer})"));
                if (report.UnnumberedOrders.Count > 10) unnumberedList += Environment.NewLine + "…";
                confirmation += Environment.NewLine + Environment.NewLine +
                    LanguageManager.Get("UnnumberedShowOrdersWarning", report.UnnumberedOrders.Count, unnumberedList);
            }
            var decision = MessageBox.Show(
                confirmation,
                LanguageManager.Get("ImportShowReport"), MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (decision != MessageBoxResult.Yes) return;
            var now = DateTimeOffset.Now;
            var session = new LiveSession
            {
                Id = $"SHOW-{report.ShowDate.Replace("-", "")}-{report.Sha256[..8]}",
                StartedAt = now,
                EndedAt = now,
                Source = "show-report",
                ShowReportSha256 = report.Sha256,
                ShowDate = report.ShowDate,
                Sales = report.Sales.ToList()
            };
            foreach (var sale in session.Sales) sale.SessionId = session.Id;
            _sessions.Add(session);
            try { _store.Save(_sessions); }
            catch { _sessions.Remove(session); throw; }
            _completedSessions.Insert(0, session);
            ShowSession(session);
            StatusText.Text = LanguageManager.Get("ShowImported", session.Id, report.Sales.Count, labelCount) +
                (report.UnnumberedOrders.Count == 0 ? "" : " " + LanguageManager.Get("UnnumberedShowOrdersSkipped", report.UnnumberedOrders.Count));
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, LanguageManager.Get("ImportShowReport"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ShowSession(LiveSession session)
    {
        _viewedHistoricalSession = session;
        SessionsGrid.SelectedItem = session;
        _currentSales.Clear();
        foreach (var sale in OrderedSales(session)) _currentSales.Add(sale);
        CurrentSalesTitle.Text = LanguageManager.Get("HistoricalSessionSales", session.Id, session.SaleCount);
        BackToCurrentSessionButton.Visibility = Visibility.Visible;
    }

    private static IEnumerable<SaleEvent> OrderedSales(LiveSession session) => session.Source == "show-report"
        ? session.Sales.OrderBy(sale => sale.ReportRowNumber)
        : session.Sales.OrderByDescending(sale => sale.DetectedAt);

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

    private static string SaleKey(SaleEvent sale) => $"{sale.BuyerName.Trim()}|{sale.ProductNumber.Trim()}|{sale.ProductName.Trim()}";

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

    private bool SaveSessions()
    {
        try
        {
            _store.Save(_sessions);
            return true;
        }
        catch (Exception ex)
        {
            StatusDot.Fill = Brushes.IndianRed;
            StatusText.Text = LanguageManager.Get("SaveFailed", ex.Message);
            return false;
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
        if (_printingAll) return;
        var selected = SalesGrid.SelectedItems.Cast<SaleEvent>().ToHashSet();
        var sales = _currentSales.Where(selected.Contains).ToArray();
        if (sales.Length == 0)
        {
            PrintStatusText.Text = LanguageManager.Get("SelectSaleFirst");
            return;
        }
        if (sales.Length == 1)
        {
            await PrintSaleAsync(sales[0]);
            return;
        }
        var labelCount = sales.Sum(sale => sale.Quantity);
        if (MessageBox.Show(LanguageManager.Get("ConfirmPrintSelected", sales.Length, labelCount),
                LanguageManager.Get("PrintSelectedSale"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        await PrintBatchAsync(sales);
    }

    private async void PrintAllButton_Click(object sender, RoutedEventArgs e)
    {
        if (_printingAll) return;
        var session = _viewedHistoricalSession ?? _activeSession;
        if (session is null)
        {
            PrintStatusText.Text = LanguageManager.Get("SelectShowFirst");
            return;
        }
        var remaining = OrderedSales(session).Where(sale => !sale.PrintedAt.HasValue).ToArray();
        if (remaining.Length == 0)
        {
            PrintStatusText.Text = LanguageManager.Get("AllAlreadyPrinted");
            return;
        }
        var labelCount = remaining.Sum(sale => sale.Quantity);
        if (MessageBox.Show(LanguageManager.Get("ConfirmPrintAll", session.Id, remaining.Length, labelCount),
                LanguageManager.Get("PrintAllUnprinted"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        await PrintBatchAsync(remaining);
    }

    private async Task PrintBatchAsync(SaleEvent[] sales)
    {
        _printingAll = true;
        PrintSelectedButton.IsEnabled = false;
        PrintAllButton.IsEnabled = false;
        try
        {
            var labelCount = sales.Sum(sale => sale.Quantity);
            PrintStatusText.Text = LanguageManager.Get("PrintingBatch", sales.Length, labelCount);
            await _remotePrinter.SendBatchAsync(RelayEndpointTextBox.Text, RelayApiKeyBox.Password, sales);
            var printedAt = DateTimeOffset.Now;
            foreach (var sale in sales) sale.PrintedAt = printedAt;
            SalesGrid.Items.Refresh();
            PrintStatusText.Text = SaveSessions()
                ? LanguageManager.Get("PrintBatchResult", sales.Length, labelCount)
                : LanguageManager.Get("BatchPrintedButNotSaved", sales.Length, labelCount);
        }
        catch (Exception ex)
        {
            PrintStatusText.Text = LanguageManager.Get("PrintFailed", ex.Message);
        }
        finally
        {
            _printingAll = false;
            PrintSelectedButton.IsEnabled = true;
            PrintAllButton.IsEnabled = true;
        }
    }

    private async Task<bool> PrintSaleAsync(SaleEvent sale)
    {
        try
        {
            PrintStatusText.Text = LanguageManager.Get("PrintingProduct", sale.ProductNumber);
            await _remotePrinter.SendAsync(RelayEndpointTextBox.Text, RelayApiKeyBox.Password, sale, false, sale.Quantity);
            sale.PrintedAt = DateTimeOffset.Now;
            SalesGrid.Items.Refresh();
            if (!SaveSessions())
            {
                PrintStatusText.Text = LanguageManager.Get("PrintedButNotSaved", sale.ProductNumber);
                return false;
            }
            PrintStatusText.Text = LanguageManager.Get("PrintAccepted", sale.ProductNumber);
            return true;
        }
        catch (Exception ex)
        {
            PrintStatusText.Text = LanguageManager.Get("PrintFailed", ex.Message);
            return false;
        }
    }

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
