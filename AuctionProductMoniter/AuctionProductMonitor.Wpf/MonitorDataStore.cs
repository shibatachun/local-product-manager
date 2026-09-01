using System.IO;
using System.Text.Json;

namespace AuctionProductMonitor.Wpf;

public sealed class MonitorDataStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AuctionProductMonitor",
        "monitor-sessions.json");

    public List<LiveSession> Load()
    {
        if (!File.Exists(FilePath)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<LiveSession>>(File.ReadAllText(FilePath), JsonOptions) ?? [];
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public void Save(IReadOnlyCollection<LiveSession> sessions)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temporaryPath = FilePath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(sessions, JsonOptions));
        File.Move(temporaryPath, FilePath, true);
    }
}
