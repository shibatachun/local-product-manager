using LocalProductManager.Api.Data;
using LocalProductManager.Api.Dtos;
using LocalProductManager.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LocalProductManager.Api.Services;

public sealed class ProductService(AppDbContext db)
{
    public async Task<IReadOnlyList<ProductListItemDto>> SearchAsync(
        string? keyword,
        string? platform,
        bool? hasAmazon,
        CancellationToken cancellationToken)
    {
        IQueryable<Product> query = db.Products
            .AsNoTracking()
            .Include(product => product.Listings)
            .Include(product => product.AmazonAsins)
            .Include(product => product.ChildProducts)
                .ThenInclude(child => child.Listings)
            .Include(product => product.ChildProducts)
                .ThenInclude(child => child.AmazonAsins)
            .Where(product => product.ParentProductId == null);

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var normalizedKeyword = keyword.Trim();
            query = query.Where(product =>
                product.Sku.Contains(normalizedKeyword) ||
                product.ProductName.Contains(normalizedKeyword) ||
                product.ChildProducts.Any(child =>
                    child.Sku.Contains(normalizedKeyword) ||
                    child.ProductName.Contains(normalizedKeyword)));
        }

        if (!string.IsNullOrWhiteSpace(platform))
        {
            var normalizedPlatform = AllowedValues.Normalize(platform);
            query = query.Where(product => product.Listings.Any(listing => listing.Platform == normalizedPlatform));
        }

        if (hasAmazon is true)
        {
            query = query.Where(product =>
                product.Listings.Any(listing => listing.Platform == "amazon") ||
                product.AmazonAsins.Any() ||
                product.ChildProducts.Any(child =>
                    child.Listings.Any(listing => listing.Platform == "amazon") ||
                    child.AmazonAsins.Any()));
        }
        else if (hasAmazon is false)
        {
            query = query.Where(product =>
                !product.Listings.Any(listing => listing.Platform == "amazon") &&
                !product.AmazonAsins.Any() &&
                !product.ChildProducts.Any(child =>
                    child.Listings.Any(listing => listing.Platform == "amazon") ||
                    child.AmazonAsins.Any()));
        }

        var products = await query
            .OrderBy(product => product.Sku)
            .ToListAsync(cancellationToken);

        return products.Select(ProductMapper.ToListItemDto).ToArray();
    }

    public async Task<ProductDetailDto?> GetAsync(int productId, CancellationToken cancellationToken)
    {
        var product = await GetProductGraph(productId)
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);

        return product is null ? null : ProductMapper.ToDetailDto(product);
    }

    public async Task<ProductDetailDto> CreateAsync(CreateProductRequest request, CancellationToken cancellationToken)
    {
        var product = new Product
        {
            Sku = NormalizeSku(request.Sku),
            ProductName = request.ProductName.Trim(),
            Cost = request.Cost,
            ImagePath = NormalizeOptional(request.ImagePath),
            ImageSource = NormalizeImageSource(request.ImageSource, request.ImagePath),
            SkuBase = GetSkuBase(request.Sku)
        };

        db.Products.Add(product);
        await db.SaveChangesAsync(cancellationToken);

        var created = await GetProductGraph(product.Id).FirstAsync(cancellationToken);
        return ProductMapper.ToDetailDto(created);
    }

    public async Task<ProductDetailDto?> UpdateAsync(int productId, UpdateProductRequest request, CancellationToken cancellationToken)
    {
        var product = await db.Products.FirstOrDefaultAsync(product => product.Id == productId, cancellationToken);
        if (product is null)
        {
            return null;
        }

        product.Sku = NormalizeSku(request.Sku);
        product.ProductName = request.ProductName.Trim();
        product.Cost = request.Cost;
        product.ImagePath = NormalizeOptional(request.ImagePath);
        product.ImageSource = NormalizeImageSource(request.ImageSource, request.ImagePath);
        product.SkuBase = GetSkuBase(request.Sku);
        product.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        var updated = await GetProductGraph(product.Id).AsNoTracking().FirstAsync(cancellationToken);
        return ProductMapper.ToDetailDto(updated);
    }

    public async Task<bool> DeleteAsync(int productId, CancellationToken cancellationToken)
    {
        await db.Products
            .Where(product => product.ParentProductId == productId)
            .ExecuteDeleteAsync(cancellationToken);

        var rows = await db.Products
            .Where(product => product.Id == productId)
            .ExecuteDeleteAsync(cancellationToken);

        return rows > 0;
    }

    private IQueryable<Product> GetProductGraph(int productId) => db.Products
        .Include(product => product.Listings)
        .Include(product => product.AmazonAsins)
        .Include(product => product.AmazonAttributionLinks)
        .Include(product => product.ChildProducts)
            .ThenInclude(child => child.Listings)
        .Include(product => product.ChildProducts)
            .ThenInclude(child => child.AmazonAsins)
        .Where(product => product.Id == productId);

    private static string NormalizeSku(string sku) => sku.Trim();

    public static string? GetSkuBase(string sku)
    {
        var match = System.Text.RegularExpressions.Regex.Match(sku.Trim(), @"^\d+");
        return match.Success ? match.Value : null;
    }

    private static string? NormalizeOptional(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static string NormalizeImageSource(string? source, string? imagePath)
    {
        var normalized = source?.Trim().ToLowerInvariant();
        if (normalized is "remote" or "local")
        {
            return normalized;
        }

        return Uri.TryCreate(imagePath, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? "remote"
            : "local";
    }
}
