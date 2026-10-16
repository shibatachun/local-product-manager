namespace LocalProductManager.Api.Models;

public sealed class LiveStreamBidManualCost
{
    public int Id { get; set; }
    public int ShowOrderId { get; set; }
    public LiveStreamBidShowOrder ShowOrder { get; set; } = null!;
    public int Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string CreatedBy { get; set; } = "guest";
}
