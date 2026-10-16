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
    string? ImagePath,
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
    string? ImportedFileSha256,
    bool IsSold,
    DateTimeOffset? SoldAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record LiveStreamBidProductImageUploadResult(
    string ImagePath,
    DateTimeOffset UpdatedAt,
    int UploadedCount);

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

public sealed record CreateLiveStreamBidListRequest(
    IReadOnlyList<CreateLiveStreamBidListRow> Rows,
    string? ExternalFileName = null,
    string? ExternalFileSha256 = null);

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

public sealed record CloudCsvAccessRequest(string Token);
public sealed record CloudCsvDownloadRequest(string Token, string RemotePath);
public sealed record CloudCsvFileDto(string RemotePath, string FileName, bool AlreadyImported);

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

public sealed record ImportLiveStreamBidProductToLocalRequest(bool TransferInventory);

public sealed record ImportLiveStreamBidProductToLocalResult(
    int ProductId, string LocalInventoryId, string Sku, int Qty, decimal Cost,
    decimal? WeightOunces, string Note);

public sealed record ConfirmLiveStreamBidSaleRequest(decimal? ActualSalePrice, int? ShowSessionId, int? ShowOrderId = null, int Quantity = 1);

public sealed record AddLiveStreamBidManualCostRequest(int Quantity, decimal UnitCost);
public sealed record UpdateLiveStreamBidManualCostRequest(decimal UnitCost);
public sealed record UpdateLiveStreamBidScannedCostRequest(decimal Cost);
public sealed record UpdateLiveStreamBidShowOrderQuantityRequest(int Quantity);

public sealed record LiveStreamBidManualCostDto(
    int Id, int Quantity, decimal UnitCost, decimal TotalCost, DateTimeOffset CreatedAt, string CreatedBy);

public sealed record LiveStreamBidShowAttachmentDto(
    int Id, string Kind, string OriginalFileName, string ContentType, DateTimeOffset UploadedAt, string UploadedBy);

public sealed record LiveStreamBidShowOrderDto(
    int Id, string ExternalOrderId, string CustomerName, string OrderName, int Quantity,
    int ScannedQuantity, int ManualQuantity, int RecordedQuantity, int RemainingQuantity,
    decimal TotalCostRmb, IReadOnlyList<LiveStreamBidSaleDto> ScannedCosts,
    IReadOnlyList<LiveStreamBidManualCostDto> ManualCosts)
{
    public IReadOnlyList<LiveStreamBidShowAttachmentDto> PriceEvidence { get; init; } = [];
}

public sealed record LiveStreamBidShowCustomerDto(
    string Name, int TotalQuantity, int RecordedQuantity, int RemainingQuantity,
    decimal TotalCostRmb, IReadOnlyList<LiveStreamBidShowOrderDto> Orders)
{
    public IReadOnlyList<LiveStreamBidShowAttachmentDto> ShipmentDocuments { get; init; } = [];
}

public sealed record LiveStreamBidShowImportResult(
    LiveStreamBidShowSummaryDto Show, int ImportedOrders, int ImportedCustomers, int IgnoredRows);

public sealed record AdjustLiveStreamBidActualInventoryRequest(int Delta);

public sealed record LiveStreamBidSaleDto(
    int Id,
    int? ShowSessionId,
    int? ShowOrderId,
    int BidProductId,
    string ProductUuid,
    string ProductTitle,
    int Quantity,
    int SoldQuantity,
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

public sealed record LiveStreamBidShowSummaryDto(
    int? Id,
    DateOnly Date,
    int ShowNumber,
    bool IsActive,
    DateTimeOffset? StartedAt,
    DateTimeOffset? EndedAt,
    string? StartedBy,
    string? EndedBy,
    string? ShowName,
    string? ImportedFileName,
    int SoldCount,
    int InventorySold,
    decimal TotalCostRmb,
    decimal TotalCostUsd,
    decimal ActualRevenueUsd,
    decimal ActualRevenueRmb,
    decimal RealizedProfitRmb,
    decimal RealizedProfitUsd,
    int OrderedQuantity,
    int RecordedCostQuantity,
    int RemainingCostQuantity,
    IReadOnlyList<LiveStreamBidShowCustomerDto> Customers,
    IReadOnlyList<LiveStreamBidSaleDto> Sales);

public sealed record LiveStreamBidDailySummaryDto(
    DateOnly Date,
    int SoldCount,
    int InventorySold,
    decimal TotalCostRmb,
    decimal TotalCostUsd,
    decimal ActualRevenueUsd,
    decimal ActualRevenueRmb,
    decimal RealizedProfitRmb,
    decimal RealizedProfitUsd,
    IReadOnlyList<LiveStreamBidShowSummaryDto> Shows,
    IReadOnlyList<LiveStreamBidSaleDto> Sales);
