using LocalProductManager.Api.Data;
using LocalProductManager.Api.Dtos;
using LocalProductManager.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LocalProductManager.Api.Services;

public sealed class BulkAttributionImportService(AppDbContext db)
{
    public async Task<BulkAttributionImportResult> ImportAsync(
        IFormFile file,
        CancellationToken cancellationToken)
    {
        await using var stream = file.OpenReadStream();
        var rows = CsvFileReader.Read(stream);
        if (rows.Count == 0 || !rows[0].ContainsKey("SKU"))
        {
            throw new InvalidOperationException("The attribution CSV must contain a SKU column.");
        }

        var matchedProducts = 0;
        var importedLinks = 0;
        var unmatched = new List<string>();

        foreach (var row in rows)
        {
            var sku = row.GetValueOrDefault("SKU")?.Trim();
            if (string.IsNullOrWhiteSpace(sku)) continue;

            var product = await db.Products.FirstOrDefaultAsync(
                item => item.Sku == sku,
                cancellationToken);
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

                var link = await db.AmazonAttributionLinks.FirstOrDefaultAsync(
                    item => item.ProductId == product.Id &&
                        item.AmazonAsinId == null &&
                        item.Platform == platform,
                    cancellationToken);
                if (link is null)
                {
                    db.AmazonAttributionLinks.Add(new AmazonAttributionLink
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
}
