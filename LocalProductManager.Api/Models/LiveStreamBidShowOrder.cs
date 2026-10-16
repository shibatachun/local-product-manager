namespace LocalProductManager.Api.Models;

public sealed class LiveStreamBidShowOrder
{
    public int Id { get; set; }
    public int ShowSessionId { get; set; }
    public LiveStreamBidShowSession ShowSession { get; set; } = null!;
    public string ExternalOrderId { get; set; } = "";
    public string CustomerName { get; set; } = "";
    public string OrderName { get; set; } = "";
    public int Quantity { get; set; }
    public int RecordedQuantity { get; set; }
    public string? PlacedAt { get; set; }
    public ICollection<LiveStreamBidSale> ScannedCosts { get; set; } = [];
    public ICollection<LiveStreamBidManualCost> ManualCosts { get; set; } = [];
}
