namespace LocalProductManager.Api.Dtos;

public sealed class ProductExtendedDataDto
{
    public ProductDetailProfileDto Details { get; set; } = new();
    public List<ProductIdentifierDto> Identifiers { get; set; } = [];
    public List<ProductSupplierDto> Suppliers { get; set; } = [];
    public ProductComplianceDto Compliance { get; set; } = new();
    public ProductPackagingDto Packaging { get; set; } = new();
    public List<ProductPriceDto> Prices { get; set; } = [];
}

public sealed class ProductDetailProfileDto
{
    public string? ExternalProductCode { get; set; }
    public string? VariantName { get; set; }
    public string? Material { get; set; }
    public string? Specification { get; set; }
    public string? Unit { get; set; }
    public string? Style { get; set; }
    public string? Description { get; set; }
    public DateOnly? DevelopmentDate { get; set; }
}
public sealed class ProductIdentifierDto { public string IdentifierType { get; set; } = "Other"; public string Value { get; set; } = ""; public bool IsPrimary { get; set; } public string? Notes { get; set; } }
public sealed class ProductSupplierDto
{
    public string Name { get; set; } = "";
    public string? ContactName { get; set; }
    public string? ContactDetails { get; set; }
    public string? ProcurementChannel { get; set; }
    public string? Buyer { get; set; }
    public decimal? MinimumPurchasePrice { get; set; }
    public int? MinimumOrderQuantity { get; set; }
    public string Currency { get; set; } = "CNY";
    public bool IsPreferred { get; set; }
    public string? Provenance { get; set; }
    public List<ProcurementLinkDto> ProcurementLinks { get; set; } = [];
}
public sealed class ProcurementLinkDto { public string Url { get; set; } = ""; public int SortOrder { get; set; } public string? Source { get; set; } }
public sealed class ProductComplianceDto
{
    public string? DeclarationName { get; set; } public string? CountryOfOrigin { get; set; } public string? HsCode { get; set; }
    public decimal? DeclaredValue { get; set; } public string? DeclaredValueCurrency { get; set; }
    public bool? ContainsBattery { get; set; } public bool? ContainsPowder { get; set; } public bool? ContainsLiquid { get; set; } public bool? ContainsMagnet { get; set; }
    public string? ShippingMethod { get; set; } public DateOnly? ExpirationDate { get; set; }
}
public sealed class ProductPackagingDto
{
    public int? PackQuantity { get; set; } public decimal? PackageLength { get; set; } public decimal? PackageWidth { get; set; } public decimal? PackageHeight { get; set; }
    public string DimensionUnit { get; set; } = "in"; public decimal? PackageWeight { get; set; } public decimal? CartonWeight { get; set; }
    public decimal? BillableWeight { get; set; } public string WeightUnit { get; set; } = "oz"; public decimal? PackagingCost { get; set; }
    public string? PackagingCostCurrency { get; set; } public string? PackagingDifficulty { get; set; } public string? LabelRequirements { get; set; }
}
public sealed class ProductPriceDto
{
    public string PriceType { get; set; } = "purchase"; public string Currency { get; set; } = "USD"; public decimal Amount { get; set; }
    public int? QuantityThreshold { get; set; } public DateOnly? EffectiveFrom { get; set; } public DateOnly? EffectiveTo { get; set; } public string? Source { get; set; }
}
