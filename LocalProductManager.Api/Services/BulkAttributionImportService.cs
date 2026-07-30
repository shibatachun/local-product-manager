using LocalProductManager.Api.Data;
using LocalProductManager.Api.Dtos;
using LocalProductManager.Api.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace LocalProductManager.Api.Services;

public sealed class BulkAttributionImportService(
    AppDbContext db,
    AmazonAttributionImportService attributionImporter)
{
    private static readonly Regex FiveDigitTokenRegex = new(@"\d{5}", RegexOptions.Compiled);

    public async Task<BulkAttributionImportResult> ImportAsync(
        IFormFile file,
        CancellationToken cancellationToken)
    {
        await using var stream = file.OpenReadStream();
        var rows = CsvFileReader.Read(stream);
        if (rows.Count == 0 || !rows[0].ContainsKey("SKU"))
        {
            var product = await MatchProductByFileNameAsync(file.FileName, cancellationToken);
            if (product is null)
            {
                throw new InvalidOperationException("The attribution CSV must contain a SKU column, or the file name must contain the product's 5-digit SKU token.");
            }

            var singleResult = await attributionImporter.ImportCsvAsync(
                product.Id,
                file,
                amazonAsinId: null,
                cancellationToken);
            return new BulkAttributionImportResult(
                rows.Count,
                singleResult is null ? 0 : 1,
                singleResult?.ImportedCount ?? 0,
                singleResult is null ? [file.FileName] : []);
        }

        var matchedProducts = 0;
        var importedLinks = 0;
        var unmatched = new List<string>();

        foreach (var row in rows)
        {
            var sku = row.GetValueOrDefault("SKU")?.Trim();
            if (string.IsNullOrWhiteSpace(sku)) continue;

            var product = await MatchProductBySkuAsync(sku, cancellationToken);
            if (product is null)
            {
                unmatched.Add(sku);
                continue;
            }

            matchedProducts++;
            foreach (var platform in AllowedValues.AttributionPlatforms)
            {
                var value = FindPlatformValue(row, platform);
                if (string.IsNullOrWhiteSpace(value)) continue;

                var link = await db.AmazonMarketingAttributionLinks.FirstOrDefaultAsync(
                    item => item.ProductId == product.Id &&
                        item.AmazonAsinId == null &&
                        item.Platform == platform,
                    cancellationToken);
                if (link is null)
                {
                    db.AmazonMarketingAttributionLinks.Add(new AmazonMarketingAttributionLink
                    {
                        ProductId = product.Id,
                        Platform = platform,
                        AttributionUrl = value,
                        ImportedFileName = file.FileName
                    });
                }
                else
                {
                    link.AttributionUrl = value;
                    link.ImportedFileName = file.FileName;
                    link.UpdatedAt = DateTimeOffset.UtcNow;
                }
                importedLinks++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return new BulkAttributionImportResult(
            rows.Count,
            matchedProducts,
            importedLinks,
            unmatched.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static string? FindPlatformValue(
        Dictionary<string, string> row,
        string platform)
    {
        var column = row.Keys.FirstOrDefault(key =>
            key.Contains(platform, StringComparison.OrdinalIgnoreCase));
        return column is null ? null : row[column].Trim();
    }

    private static string? FindClickThroughValue(Dictionary<string, string> row)
    {
        var column = row.Keys.FirstOrDefault(AmazonAttributionImportService.IsClickThroughHeader);
        return column is null ? null : AmazonAttributionImportService.CleanUrl(row[column]);
    }

    private async Task<AmazonMarketingProduct?> MatchProductBySkuAsync(string sku, CancellationToken cancellationToken)
    {
        var product = await db.AmazonMarketingProducts.FirstOrDefaultAsync(item => item.Sku == sku, cancellationToken);
        if (product is not null) return product;

        var token = ExtractFiveDigitToken(sku);
        return token is null ? null : await MatchProductByTokenAsync(token, cancellationToken);
    }

    private async Task<AmazonMarketingProduct?> MatchProductByFileNameAsync(string fileName, CancellationToken cancellationToken)
    {
        var token = ExtractFiveDigitToken(Path.GetFileNameWithoutExtension(fileName));
        return token is null ? null : await MatchProductByTokenAsync(token, cancellationToken);
    }

    private async Task<AmazonMarketingProduct?> MatchProductByTokenAsync(string token, CancellationToken cancellationToken)
    {
        var products = await db.AmazonMarketingProducts
            .Where(product => product.Sku.Contains(token))
            .OrderByDescending(product => product.ParentProductId == null)
            .ThenByDescending(product => product.ProductType == "parent")
            .ThenBy(product => product.Sku.Length)
            .ThenBy(product => product.Sku)
            .ToListAsync(cancellationToken);
        return products.FirstOrDefault();
    }

    private static string? ExtractFiveDigitToken(string value)
    {
        var match = FiveDigitTokenRegex.Match(value);
        return match.Success ? match.Value : null;
    }
}
