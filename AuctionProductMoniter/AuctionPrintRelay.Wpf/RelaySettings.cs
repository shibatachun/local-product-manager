using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;

namespace AuctionPrintRelay.Wpf;

public sealed class RelaySettings
{
    public string ApiKey { get; set; } = "";
    public string BarTenderEndpoint { get; set; } = "http://127.0.0.1:5164/Integration/auction-product-label/Execute";
    public const int Port = 17892;
    public string RelayAddress => $"http://{LanIp()}:{Port}/api/print";
    private static string PathName => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AuctionPrintRelay", "settings.json");

    public static RelaySettings Load()
    {
        RelaySettings value;
        try { value = JsonSerializer.Deserialize<RelaySettings>(File.ReadAllText(PathName)) ?? new(); }
        catch { value = new(); }
        if (value.ApiKey.Length < 24) value.ApiKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(18));
        value.Save();
        return value;
    }
    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PathName)!);
        File.WriteAllText(PathName, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
    private static string LanIp() => NetworkInterface.GetAllNetworkInterfaces()
        .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
        .OrderByDescending(n => n.GetIPProperties().GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork))
        .SelectMany(n => n.GetIPProperties().UnicastAddresses).Select(a => a.Address)
        .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a) && !a.ToString().StartsWith("169.254."))?.ToString() ?? "THIS-PC-IP";
}
