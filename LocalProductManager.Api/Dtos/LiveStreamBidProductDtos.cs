namespace LocalProductManager.Api.Dtos;

public sealed record LiveStreamBidProductDto(
    int Id,
    int? SourceProductId,
    string? BatchNumber,
    int? BatchProductNumber,
    string? ActualSku,
    bool RequiresLocalLabel,
    decimal? WhatnotAuctionSafePrice,
    string? ItemCode,
    string Uuid,
    string Title,
    int Quantity,
    int Inventory,
    int ActualInventory,
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

public sealed record CreateLiveStreamBidListRow(
    int? SourceProductId,
    string? ExternalUuid,
    string? ActualSku,
    decimal? WhatnotAuctionSafePrice,
    string Title,
    int Quantity,
    int ExpectedInventory,
    int ActualInventory,
    decimal UnitPrice,
    decimal? WeightGrams,
    decimal? WeightOunces,
    string? Material,
    decimal? TotalCost);

public sealed record CreateLiveStreamBidListRequest(IReadOnlyList<CreateLiveStreamBidListRow> Rows);

public sealed record UpdateLiveStreamBidProductRequest(
    string? ActualSku,
    decimal? WhatnotAuctionSafePrice,
    string Title,
    int Quantity,
    int ExpectedInventory,
    int ActualInventory,
    decimal UnitPrice,
    decimal? WeightGrams,
    decimal? WeightOunces,
    string? Material,
    decimal? TotalCost);

public sealed record CreateLiveStreamBidListResult(
    string BatchNumber,
    IReadOnlyList<LiveStreamBidProductDto> Products,
    IReadOnlyList<LiveStreamBidRegeneratedUuidDto> RegeneratedUuids);

public sealed record LiveStreamBidImportResult(
    int Created,
    int Updated,
    int Skipped,
    int TotalRows,
    IReadOnlyList<LiveStreamBidImportConflictDto> Conflicts,
    IReadOnlyList<LiveStreamBidRegeneratedUuidDto> RegeneratedUuids);

public sealed record LiveStreamBidRegeneratedUuidDto(
    int RowNumber,
    int ProductId,
    string OriginalUuid,
    string NewUuid,
    string Title);

public sealed record LiveStreamBidImportConflictDto(
    int RowNumber,
    string? Uuid,
    string? ActualSku,
    string Title,
    IReadOnlyList<string> Reasons);

public sealed record MoveProductToLiveStreamBidRequest(
    int ProductId,
    int LevelId,
    int BinNumber,
    int Quantity);

public sealed record ConfirmLiveStreamBidSaleRequest(decimal? ActualSalePrice);

public sealed record AdjustLiveStreamBidActualInventoryRequest(int Delta);

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
