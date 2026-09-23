using System.Collections.ObjectModel;
using System.Windows;
namespace AuctionPrintRelay.Wpf;
public partial class MainWindow : Window
{
    private readonly RelaySettings _settings = RelaySettings.Load();
    private readonly BarTenderClient _barTender = new();
    private readonly ObservableCollection<PrintLog> _logs = [];
    private RelayServer? _server;
    public MainWindow()
    {
        InitializeComponent(); AddressBox.Text = _settings.RelayAddress; KeyBox.Text = _settings.ApiKey;
        EndpointBox.Text = _settings.BarTenderEndpoint; LogGrid.ItemsSource = _logs; Loaded += OnLoaded; Closed += OnClosed;
    }
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try { _server = new(_settings, _barTender); _server.Logged += OnLogged; await _server.StartAsync(); StatusText.Text = $"中转器运行中：{_settings.RelayAddress}"; }
        catch (Exception ex) { StatusText.Text = "启动失败：" + ex.Message; }
    }
    private void OnLogged(PrintLog item) => Dispatcher.Invoke(() => _logs.Insert(0, item));
    private void CopyKey_Click(object sender, RoutedEventArgs e) { Clipboard.SetText(KeyBox.Text); StatusText.Text = "访问密钥已复制。"; }
    private async void SaveTest_Click(object sender, RoutedEventArgs e)
    {
        try { _settings.BarTenderEndpoint = EndpointBox.Text.Trim(); _settings.Save(); await _barTender.ProbeAsync(_settings.BarTenderEndpoint); StatusText.Text="BarTender 端口连接成功（未打印）。"; }
        catch (Exception ex) { StatusText.Text="BarTender 测试失败："+ex.Message; }
    }
    private async void OnClosed(object? sender, EventArgs e) { if (_server is not null) { _server.Logged -= OnLogged; await _server.DisposeAsync(); } }
}
