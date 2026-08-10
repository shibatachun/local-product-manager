namespace LocalProductManager.Api.Dtos;

public sealed record LiveStreamBidProductDto(
    int Id,
    int? SourceProductId,
    string Uuid,
    string Title,
    int Quantity,
    int Inventory,
    decimal UnitPrice,
    decimal? WeightGrams,
    decimal? WeightOunces,
    string? Material,
    decimal? TotalCost,
    string? ImportedFileName,
    bool IsSold,
    DateTimeOffset? SoldAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record LiveStreamBidImportResult(
    int Created,
    int Updated,
    int Skipped,
    int TotalRows);

public sealed record MoveProductToLiveStreamBidRequest(
    int ProductId,
    int LevelId,
    int BinNumber,
    int Quantity);

public sealed record ConfirmLiveStreamBidSaleRequest(decimal? ActualSalePrice);

public sealed record LiveStreamBidSaleDto(
    int Id,
    int BidProductId,
    string ProductUuid,
    string ProductTitle,
    int Quantity,
    decimal? Cost,
    decimal? ActualSalePriceUsd,
    decimal? SalePriceRmb,
    decimal? ExchangeRateRmbToUsd,
    DateOnly? ExchangeRateDate,
    decimal? ProfitRmb,
    decimal? ProfitUsd,
    DateOnly SaleDate,
    DateTimeOffset SoldAt,
    string SoldBy);

public sealed record LiveStreamBidDailySummaryDto(
    DateOnly Date,
    int SoldCount,
    int TotalUnits,
    decimal TotalCostRmb,
    decimal TotalCostUsd,
    decimal ActualRevenueUsd,
    decimal ActualRevenueRmb,
    decimal RealizedProfitRmb,
    decimal RealizedProfitUsd,
    IReadOnlyList<LiveStreamBidSaleDto> Sales);
