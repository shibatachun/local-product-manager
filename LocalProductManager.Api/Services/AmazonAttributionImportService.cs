using System.Text;
using LocalProductManager.Api.Data;
using LocalProductManager.Api.Dtos;
using LocalProductManager.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LocalProductManager.Api.Services;

public sealed class AmazonAttributionImportService(AppDbContext db)
{
    public async Task<ImportAmazonAttributionResult?> ImportCsvAsync(
        int productId,
        IFormFile file,
        int? amazonAsinId,
        CancellationToken cancellationToken)
    {
        var productExists = await db.Products.AnyAsync(product => product.Id == productId, cancellationToken);
        if (!productExists)
        {
            return null;
        }

        if (amazonAsinId.HasValue)
        {
            var asinExists = await db.AmazonAsins.AnyAsync(
                asin => asin.Id == amazonAsinId.Value && asin.ProductId == productId,
                cancellationToken);

            if (!asinExists)
            {
                throw new InvalidOperationException("ASIN does not belong to this product.");
            }
        }

        await using var stream = file.OpenReadStream();
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var csv = await reader.ReadToEndAsync(cancellationToken);
        var rows = ParseCsv(csv);
        if (rows.Count == 0)
        {
            return new ImportAmazonAttributionResult(0, []);
        }

        var header = rows[0].Select(NormalizeHeader).ToArray();
        var imported = new List<AmazonAttributionLink>();

        foreach (var row in rows.Skip(1))
        {
            for (var columnIndex = 0; columnIndex < Math.Min(header.Length, row.Count); columnIndex++)
            {
                var platform = PlatformFromHeader(header[columnIndex]);
                if (platform is null)
                {
                    continue;
                }

                var url = row[columnIndex].Trim();
                if (string.IsNullOrWhiteSpace(url))
                {
                    continue;
                }

                var link = await UpsertLinkAsync(
                    productId,
                    amazonAsinId,
                    platform,
                    url,
                    file.FileName,
                    cancellationToken);

                imported.Add(link);
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        return new ImportAmazonAttributionResult(imported.Count, imported.Select(ProductMapper.ToDto).ToArray());
    }

    public async Task<AmazonAttributionLinkDto?> UpsertAsync(
        int productId,
        string platform,
        UpsertAmazonAttributionRequest request,
        CancellationToken cancellationToken)
    {
        var normalizedPlatform = AllowedValues.Normalize(platform);
        if (!AllowedValues.AttributionPlatforms.Contains(normalizedPlatform))
        {
            throw new ArgumentException($"Unsupported attribution platform: {platform}");
        }

        var productExists = await db.Products.AnyAsync(product => product.Id == productId, cancellationToken);
        if (!productExists)
        {
            return null;
        }

        var link = await UpsertLinkAsync(
            productId,
            request.AmazonAsinId,
            normalizedPlatform,
            request.AttributionUrl.Trim(),
            importedFileName: null,
            cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return ProductMapper.ToDto(link);
    }

    private async Task<AmazonAttributionLink> UpsertLinkAsync(
        int productId,
        int? amazonAsinId,
        string platform,
        string attributionUrl,
        string? importedFileName,
        CancellationToken cancellationToken)
    {
        var link = await db.AmazonAttributionLinks.FirstOrDefaultAsync(existing =>
            existing.ProductId == productId &&
            existing.AmazonAsinId == amazonAsinId &&
            existing.Platform == platform,
            cancellationToken);

        if (link is null)
        {
            link = new AmazonAttributionLink
            {
                ProductId = productId,
                AmazonAsinId = amazonAsinId,
                Platform = platform,
                AttributionUrl = attributionUrl,
                ImportedFileName = importedFileName
            };
            db.AmazonAttributionLinks.Add(link);
        }
        else
        {
            link.AttributionUrl = attributionUrl;
            link.ImportedFileName = importedFileName ?? link.ImportedFileName;
            link.UpdatedAt = DateTimeOffset.UtcNow;
        }

        return link;
    }

    private static string NormalizeHeader(string value) => value.Trim().ToLowerInvariant().Replace(" ", "");

    private static string? PlatformFromHeader(string header)
    {
        foreach (var platform in AllowedValues.AttributionPlatforms)
        {
            if (header.Contains(platform, StringComparison.OrdinalIgnoreCase))
            {
                return platform;
            }
        }

        return null;
    }

    private static List<List<string>> ParseCsv(string csv)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();
        var inQuotes = false;

        for (var index = 0; index < csv.Length; index++)
        {
            var current = csv[index];
            if (current == '"')
            {
                if (inQuotes && index + 1 < csv.Length && csv[index + 1] == '"')
                {
                    cell.Append('"');
                    index++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (current == ',' && !inQuotes)
            {
                row.Add(cell.ToString());
                cell.Clear();
            }
            else if ((current == '\r' || current == '\n') && !inQuotes)
            {
                if (current == '\r' && index + 1 < csv.Length && csv[index + 1] == '\n')
                {
                    index++;
                }

                row.Add(cell.ToString());
                cell.Clear();
                if (row.Any(value => !string.IsNullOrWhiteSpace(value)))
                {
                    rows.Add(row);
                }

                row = [];
            }
            else
            {
                cell.Append(current);
            }
        }

        row.Add(cell.ToString());
        if (row.Any(value => !string.IsNullOrWhiteSpace(value)))
        {
            rows.Add(row);
        }

        return rows;
    }
}
