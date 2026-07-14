using LocalProductManager.Api.Data;
using LocalProductManager.Api.Dtos;
using LocalProductManager.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LocalProductManager.Api.Services;

public sealed class AmazonBusinessReportImportService(AppDbContext db)
{
    public async Task<BusinessReportImportResult> ImportAsync(
        IFormFile file,
        CancellationToken cancellationToken)
    {
        await using var stream = file.OpenReadStream();
        var rows = CsvFileReader.Read(stream)
            .Where(row =>
                !string.IsNullOrWhiteSpace(Value(row, "SKU")) &&
                !string.IsNullOrWhiteSpace(Value(row, "(Child) ASIN")))
            .ToList();

        ValidateHeaders(rows);

        var parentCreated = 0;
        var childCreated = 0;
        var standaloneCreated = 0;
        var updated = 0;
        var variationGroups = 0;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        foreach (var group in rows.GroupBy(row => Value(row, "(Parent) ASIN"), StringComparer.OrdinalIgnoreCase))
        {
            var groupRows = group.ToList();
            var parentAsin = group.Key.Trim().ToUpperInvariant();
            var isVariation = groupRows.Count > 1 ||
                groupRows.Any(row =>
                    !string.Equals(
                        Value(row, "(Parent) ASIN"),
                        Value(row, "(Child) ASIN"),
                        StringComparison.OrdinalIgnoreCase));

            Product? parentProduct = null;
            if (isVariation)
            {
                variationGroups++;
                var parentSku = $"PARENT-{parentAsin}";
                parentProduct = await db.Products
                    .Include(product => product.Listings)
                    .Include(product => product.AmazonAsins)
                    .FirstOrDefaultAsync(product => product.Sku == parentSku, cancellationToken);

                if (parentProduct is null)
                {
                    parentProduct = new Product
                    {
                        Sku = parentSku,
                        ProductName = BuildParentTitle(groupRows),
                        ProductType = "parent",
                        AmazonParentAsin = parentAsin
                    };
                    db.Products.Add(parentProduct);
                    parentCreated++;
                }
                else
                {
                    parentProduct.ProductName = BuildParentTitle(groupRows);
                    parentProduct.ProductType = "parent";
                    parentProduct.AmazonParentAsin = parentAsin;
                    parentProduct.UpdatedAt = DateTimeOffset.UtcNow;
                    updated++;
                }

                EnsureAmazonListing(parentProduct);
                EnsureAsin(parentProduct, parentAsin, "parent", null);
                await db.SaveChangesAsync(cancellationToken);
                EnsureLocalInventoryId(parentProduct);
                await db.SaveChangesAsync(cancellationToken);
            }

            foreach (var row in groupRows)
            {
                var sku = Value(row, "SKU");
                var childAsin = Value(row, "(Child) ASIN").ToUpperInvariant();
                var title = Value(row, "Title");
                var product = await db.Products
                    .Include(item => item.Listings)
                    .Include(item => item.AmazonAsins)
                    .FirstOrDefaultAsync(item => item.Sku == sku, cancellationToken);

                if (product is null)
                {
                    product = new Product
                    {
                        Sku = sku,
                        ProductName = string.IsNullOrWhiteSpace(title) ? sku : title,
                        ProductType = isVariation ? "child" : "standalone",
                        ParentProductId = parentProduct?.Id,
                        SkuBase = ProductService.GetSkuBase(sku),
                        AmazonParentAsin = isVariation ? parentAsin : null
                    };
                    db.Products.Add(product);
                    if (isVariation) childCreated++; else standaloneCreated++;
                }
                else
                {
                    product.ProductName = string.IsNullOrWhiteSpace(title) ? product.ProductName : title;
                    product.ProductType = isVariation ? "child" : "standalone";
                    product.ParentProductId = parentProduct?.Id;
                    product.SkuBase = ProductService.GetSkuBase(sku);
                    product.AmazonParentAsin = isVariation ? parentAsin : null;
                    product.UpdatedAt = DateTimeOffset.UtcNow;
                    updated++;
                }

                EnsureAmazonListing(product);
                EnsureAsin(product, childAsin, "child", isVariation ? parentAsin : null);
                await db.SaveChangesAsync(cancellationToken);
                EnsureLocalInventoryId(product);
                await db.SaveChangesAsync(cancellationToken);
            }
        }

        await transaction.CommitAsync(cancellationToken);

        return new BusinessReportImportResult(
            rows.Count,
            parentCreated,
            childCreated,
            standaloneCreated,
            updated,
            variationGroups);
    }

    private static void ValidateHeaders(List<Dictionary<string, string>> rows)
    {
        if (rows.Count == 0)
        {
            throw new InvalidOperationException("The Business Report does not contain product rows.");
        }

        var first = rows[0];
        foreach (var header in new[] { "(Parent) ASIN", "(Child) ASIN", "Title", "SKU" })
        {
            if (!first.ContainsKey(header))
            {
                throw new InvalidOperationException($"Required Business Report column is missing: {header}");
            }
        }
    }

    private static string Value(Dictionary<string, string> row, string key) =>
        row.TryGetValue(key, out var value) ? value.Trim() : string.Empty;

    private static string BuildParentTitle(List<Dictionary<string, string>> rows)
    {
        var title = Value(rows[0], "Title");
        return string.IsNullOrWhiteSpace(title)
            ? $"Amazon variation {Value(rows[0], "(Parent) ASIN")}"
            : title;
    }

    private static void EnsureAmazonListing(Product product)
    {
        if (product.Listings.All(listing => listing.Platform != "amazon"))
        {
            product.Listings.Add(new ProductListing
            {
                Platform = "amazon"
            });
        }
    }

    private static void EnsureLocalInventoryId(Product product)
    {
        if (string.IsNullOrWhiteSpace(product.LocalInventoryId))
        {
            product.LocalInventoryId = ProductService.BuildLocalInventoryId(product.Id);
        }
    }

    private static void EnsureAsin(Product product, string asin, string asinType, string? parentAsin)
    {
        var existing = product.AmazonAsins.FirstOrDefault(
            item => string.Equals(item.Asin, asin, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            product.AmazonAsins.Add(new AmazonAsin
            {
                Asin = asin,
                AsinType = asinType,
                ParentAsin = parentAsin
            });
        }
        else
        {
            existing.AsinType = asinType;
            existing.ParentAsin = parentAsin;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }
    }
}
