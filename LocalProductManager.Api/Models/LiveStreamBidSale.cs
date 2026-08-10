namespace LocalProductManager.Api.Models;

public sealed class LiveStreamBidSale
{
    public int Id { get; set; }
    public int BidProductId { get; set; }
    public LiveStreamBidProduct BidProduct { get; set; } = null!;
    public string ProductUuid { get; set; } = "";
    public string ProductTitle { get; set; } = "";
    public int Quantity { get; set; }
    public decimal? Cost { get; set; }
    public decimal? ActualSalePrice { get; set; }
    public decimal? SalePriceRmb { get; set; }
    public decimal? ExchangeRateRmbToUsd { get; set; }
    public DateOnly? ExchangeRateDate { get; set; }
    public decimal? Profit { get; set; }
    public decimal? ProfitUsd { get; set; }
    public DateOnly SaleDate { get; set; }
    public DateTimeOffset SoldAt { get; set; } = DateTimeOffset.UtcNow;
    public string SoldBy { get; set; } = "guest";
}
