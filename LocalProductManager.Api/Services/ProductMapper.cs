using LocalProductManager.Api.Dtos;
using LocalProductManager.Api.Models;

namespace LocalProductManager.Api.Services;

public static class ProductMapper
{
    public static ProductListItemDto ToListItemDto(Product product) => new(
        product.Id,
        product.LocalInventoryId,
        product.Sku,
        product.ProductName,
        product.Category,
        product.InventoryCategory,
        product.CategoryInventoryId,
        product.Cost,
        product.OriginalCost,
        product.SalePrice,
        product.Qty,
        product.ReadyToList,
        product.Note,
        product.Weight,
        product.Length,
        product.Width,
        product.Height,
        product.ImagePath,
        product.ImageSource,
        product.Listings.Select(listing => listing.Platform).Order().ToArray(),
        product.AmazonAsins.Select(asin => asin.Asin).Order().ToArray(),
        product.AmazonAsins.Count,
        product.ProductType,
        product.SkuBase,
        product.AmazonParentAsin,
        product.ParentProductId,
        product.ChildProducts.Select(ToListItemDto).OrderBy(child => child.Sku).ToArray(),
        product.CreatedAt,
        product.UpdatedAt);

    public static ProductDetailDto ToDetailDto(Product product) => new(
        product.Id,
        product.LocalInventoryId,
        product.Sku,
        product.ProductName,
        product.Category,
        product.InventoryCategory,
        product.CategoryInventoryId,
        product.Cost,
        product.OriginalCost,
        product.SalePrice,
        product.Qty,
        product.ReadyToList,
        product.Note,
        product.Weight,
        product.Length,
        product.Width,
        product.Height,
        product.ImagePath,
        product.ImageSource,
        product.Listings.Select(ToDto).OrderBy(listing => listing.Platform).ToArray(),
        product.AmazonAsins.Select(ToDto).OrderBy(asin => asin.AsinType).ThenBy(asin => asin.Asin).ToArray(),
        product.AmazonAttributionLinks.Select(ToDto).OrderBy(link => link.Platform).ToArray(),
        product.ProductType,
        product.SkuBase,
        product.AmazonParentAsin,
        product.ParentProductId,
        product.ChildProducts.Select(ToListItemDto).OrderBy(child => child.Sku).ToArray(),
        product.CreatedAt,
        product.UpdatedAt);

    public static ProductListingDto ToDto(ProductListing listing) => new(
        listing.Id,
        listing.ProductId,
        listing.Platform,
        listing.ListingUrl,
        listing.RetailPrice,
        listing.CreatedAt,
        listing.UpdatedAt);

    public static AmazonAsinDto ToDto(AmazonAsin asin) => new(
        asin.Id,
        asin.ProductId,
        asin.Asin,
        asin.AsinType,
        asin.ParentAsin,
        asin.AmazonAttributeDone,
        asin.CreatedAt,
        asin.UpdatedAt);

    public static AmazonAttributionLinkDto ToDto(AmazonAttributionLink link) => new(
        link.Id,
        link.ProductId,
        link.AmazonAsinId,
        link.Platform,
        link.AttributionUrl,
        link.ImportedFileName,
        link.CreatedAt,
        link.UpdatedAt);
}
