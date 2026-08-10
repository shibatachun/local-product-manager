namespace LocalProductManager.Api.Models;

public sealed class ProductDetailProfile
{
    public int ProductId { get; set; }
    public string? ExternalProductCode { get; set; }
    public string? VariantName { get; set; }
    public string? Material { get; set; }
    public string? Specification { get; set; }
    public string? Unit { get; set; }
    public string? Style { get; set; }
    public string? Description { get; set; }
    public DateOnly? DevelopmentDate { get; set; }
    public Product Product { get; set; } = null!;
}

public sealed class ProductIdentifier
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public required string IdentifierType { get; set; }
    public required string Value { get; set; }
    public bool IsPrimary { get; set; }
    public string? Notes { get; set; }
    public Product Product { get; set; } = null!;
}

public sealed class Supplier
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? ContactName { get; set; }
    public string? ContactDetails { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ProductSupplier
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public int SupplierId { get; set; }
    public string? ProcurementChannel { get; set; }
    public string? Buyer { get; set; }
    public decimal? MinimumPurchasePrice { get; set; }
    public int? MinimumOrderQuantity { get; set; }
    public string Currency { get; set; } = "CNY";
    public bool IsPreferred { get; set; }
    public string? Provenance { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Product Product { get; set; } = null!;
    public Supplier Supplier { get; set; } = null!;
    public List<ProcurementLink> ProcurementLinks { get; set; } = [];
}

public sealed class ProcurementLink
{
    public int Id { get; set; }
    public int ProductSupplierId { get; set; }
    public required string Url { get; set; }
    public int SortOrder { get; set; }
    public string? Source { get; set; }
    public ProductSupplier ProductSupplier { get; set; } = null!;
}

public sealed class ProductCompliance
{
    public int ProductId { get; set; }
    public string? DeclarationName { get; set; }
    public string? CountryOfOrigin { get; set; }
    public string? HsCode { get; set; }
    public decimal? DeclaredValue { get; set; }
    public string? DeclaredValueCurrency { get; set; }
    public bool? ContainsBattery { get; set; }
    public bool? ContainsPowder { get; set; }
    public bool? ContainsLiquid { get; set; }
    public bool? ContainsMagnet { get; set; }
    public string? ShippingMethod { get; set; }
    public DateOnly? ExpirationDate { get; set; }
    public Product Product { get; set; } = null!;
}

public sealed class ProductPackaging
{
    public int ProductId { get; set; }
    public int? PackQuantity { get; set; }
    public decimal? PackageLength { get; set; }
    public decimal? PackageWidth { get; set; }
    public decimal? PackageHeight { get; set; }
    public string DimensionUnit { get; set; } = "in";
    public decimal? PackageWeight { get; set; }
    public decimal? CartonWeight { get; set; }
    public decimal? BillableWeight { get; set; }
    public string WeightUnit { get; set; } = "oz";
    public decimal? PackagingCost { get; set; }
    public string? PackagingCostCurrency { get; set; }
    public string? PackagingDifficulty { get; set; }
    public string? LabelRequirements { get; set; }
    public Product Product { get; set; } = null!;
}

public sealed class ProductPrice
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public required string PriceType { get; set; }
    public required string Currency { get; set; }
    public decimal Amount { get; set; }
    public int? QuantityThreshold { get; set; }
    public DateOnly? EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public string? Source { get; set; }
    public Product Product { get; set; } = null!;
}
