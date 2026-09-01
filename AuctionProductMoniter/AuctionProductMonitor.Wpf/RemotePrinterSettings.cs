using System.IO;
using System.Text.Json;

namespace AuctionProductMonitor.Wpf;

public sealed class RemotePrinterSettings
{
    public string Endpoint { get; set; } = "http://PRINT-PC-IP:17892/api/print";
    public string ApiKey { get; set; } = "";

    private static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AuctionProductMonitor", "remote-printer.json");

    public static RemotePrinterSettings Load()
    {
        try { return JsonSerializer.Deserialize<RemotePrinterSettings>(File.ReadAllText(FilePath)) ?? new(); }
        catch { return new(); }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
