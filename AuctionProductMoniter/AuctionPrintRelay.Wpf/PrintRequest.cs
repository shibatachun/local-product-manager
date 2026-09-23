namespace AuctionPrintRelay.Wpf;
public sealed class PrintRequest
{
    public string BuyerName { get; set; } = "";
    public string ProductNumber { get; set; } = "";
    public string SessionId { get; set; } = "remote-print";
    public int Copies { get; set; } = 1;
    public bool Test { get; set; }
}
public sealed class BulkPrintRequest
{
    public string SessionId { get; set; } = "remote-print";
    public List<BulkPrintItem> Items { get; set; } = [];
}
public sealed class BulkPrintItem
{
    public string BuyerName { get; set; } = "";
    public string ProductNumber { get; set; } = "";
    public int Copies { get; set; } = 1;
}
public sealed record PrintLog(DateTimeOffset Time, string Buyer, string Product, string Status)
{
    public string LocalTime => Time.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
}
