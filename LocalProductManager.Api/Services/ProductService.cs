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

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            products = products
                .Where(product => ProductMatches(product, keyword))
                .ToList();
        }

        return products.Select(ProductMapper.ToListItemDto).ToArray();
    }

    public async Task<ProductDetailDto?> GetAsync(int productId, CancellationToken cancellationToken)
    {
        var product = await GetProductGraph(productId)
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);

        return product is null ? null : ProductMapper.ToDetailDto(product);
    }

    public async Task<ProductDuplicateCheckDto> CheckDuplicateAsync(
        string sku,
        int? excludeProductId,
        CancellationToken cancellationToken)
    {
        var normalizedSku = NormalizeSku(sku);
        var skuBase = GetSkuBase(normalizedSku);
        var query = db.Products.AsNoTracking();
        if (excludeProductId.HasValue)
        {
            query = query.Where(product => product.Id != excludeProductId.Value);
        }

        var exact = await query
            .Where(product => product.Sku == normalizedSku)
            .Select(product => new ProductDuplicateCandidateDto(
                product.Id,
                product.Sku,
                product.ProductName,
                product.SkuBase))
            .FirstOrDefaultAsync(cancellationToken);

        var sameBaseProducts = string.IsNullOrWhiteSpace(skuBase)
            ? []
            : await query
                .Where(product => product.SkuBase == skuBase)
                .OrderBy(product => product.Sku)
                .Take(10)
                .Select(product => new ProductDuplicateCandidateDto(
                    product.Id,
                    product.Sku,
                    product.ProductName,
                    product.SkuBase))
                .ToListAsync(cancellationToken);

        return new ProductDuplicateCheckDto(
            normalizedSku,
            skuBase,
            exact is not null,
            exact?.Id,
            exact?.ProductName,
            sameBaseProducts);
    }

    public async Task<ProductDetailDto> CreateAsync(CreateProductRequest request, CancellationToken cancellationToken)
    {
        var duplicate = await CheckDuplicateAsync(request.Sku, null, cancellationToken);
        if (duplicate.ExactSkuExists)
        {
            throw new DuplicateSkuException(duplicate.Sku, duplicate.ExactProductId);
        }

        var localInventoryId = NormalizeOptional(request.LocalInventoryId);
        var product = new Product
        {
            LocalInventoryId = localInventoryId,
            Sku = NormalizeSku(request.Sku),
            ProductName = request.ProductName.Trim(),
            Category = NormalizeOptional(request.Category),
            Cost = request.Cost,
            OriginalCost = request.OriginalCost,
            SalePrice = request.SalePrice,
            Weight = request.Weight,
            Length = request.Length,
            Width = request.Width,
            Height = request.Height,
            ImagePath = NormalizeOptional(request.ImagePath),
            ImageSource = NormalizeImageSource(request.ImageSource, request.ImagePath),
            SkuBase = GetSkuBase(request.Sku)
        };

        db.Products.Add(product);
        await db.SaveChangesAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(product.LocalInventoryId))
        {
            product.LocalInventoryId = BuildLocalInventoryId(product.Id);
            product.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }

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

        var duplicate = await CheckDuplicateAsync(request.Sku, productId, cancellationToken);
        if (duplicate.ExactSkuExists)
        {
            throw new DuplicateSkuException(duplicate.Sku, duplicate.ExactProductId);
        }

        product.LocalInventoryId = NormalizeOptional(request.LocalInventoryId) ?? product.LocalInventoryId ?? BuildLocalInventoryId(product.Id);
        product.Sku = NormalizeSku(request.Sku);
        product.ProductName = request.ProductName.Trim();
        product.Category = NormalizeOptional(request.Category);
        product.Cost = request.Cost;
        product.OriginalCost = request.OriginalCost;
        product.SalePrice = request.SalePrice;
        product.Weight = request.Weight;
        product.Length = request.Length;
        product.Width = request.Width;
        product.Height = request.Height;
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

    private static bool ProductMatches(Product product, string keyword)
    {
        var values = new List<string?>
        {
            product.Sku,
            product.ProductName,
            product.SkuBase,
            product.AmazonParentAsin
        };
        values.AddRange(product.AmazonAsins.Select(asin => asin.Asin));
        foreach (var child in product.ChildProducts)
        {
            values.Add(child.Sku);
            values.Add(child.ProductName);
            values.Add(child.SkuBase);
            values.Add(child.AmazonParentAsin);
            values.AddRange(child.AmazonAsins.Select(asin => asin.Asin));
        }

        return values.Any(value => FuzzyMatches(value, keyword));
    }

    private static bool FuzzyMatches(string? value, string keyword)
    {
        if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(keyword)) return false;
        var haystack = NormalizeSearchText(value);
        var needle = NormalizeSearchText(keyword);
        if (haystack.Contains(needle, StringComparison.OrdinalIgnoreCase)) return true;

        var tokens = needle
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length > 1 && tokens.All(token => haystack.Contains(token, StringComparison.OrdinalIgnoreCase))) return true;

        var compactHaystack = CompactSearchText(haystack);
        var compactNeedle = CompactSearchText(needle);
        if (compactHaystack.Contains(compactNeedle, StringComparison.OrdinalIgnoreCase)) return true;
        if (IsSubsequence(compactHaystack, compactNeedle)) return true;

        var maxDistance = compactNeedle.Length <= 5 ? 1 : compactNeedle.Length <= 10 ? 2 : 3;
        return LevenshteinDistance(compactHaystack, compactNeedle, maxDistance) <= maxDistance;
    }

    private static string NormalizeSearchText(string value) =>
        value.Trim().ToLowerInvariant();

    private static string CompactSearchText(string value) =>
        new(value.Where(char.IsLetterOrDigit).ToArray());

    private static bool IsSubsequence(string value, string query)
    {
        if (query.Length < 3) return false;
        var index = 0;
        foreach (var character in value)
        {
            if (character == query[index] && ++index == query.Length) return true;
        }

        return false;
    }

    private static int LevenshteinDistance(string left, string right, int limit)
    {
        if (Math.Abs(left.Length - right.Length) > limit) return limit + 1;
        var previous = Enumerable.Range(0, right.Length + 1).ToArray();
        var current = new int[right.Length + 1];

        for (var i = 1; i <= left.Length; i++)
        {
            current[0] = i;
            var rowMin = current[0];
            for (var j = 1; j <= right.Length; j++)
            {
                var cost = left[i - 1] == right[j - 1] ? 0 : 1;
                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + cost);
                rowMin = Math.Min(rowMin, current[j]);
            }

            if (rowMin > limit) return limit + 1;
            (previous, current) = (current, previous);
        }

        return previous[right.Length];
    }

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

    public static string BuildLocalInventoryId(int productId) => $"INV-{productId}";

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

public sealed class DuplicateSkuException(string sku, int? existingProductId)
    : InvalidOperationException($"A product with SKU {sku} already exists.")
{
    public string Sku { get; } = sku;
    public int? ExistingProductId { get; } = existingProductId;
}
