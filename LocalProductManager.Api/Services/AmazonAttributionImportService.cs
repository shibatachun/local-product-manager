using System.Text;
using System.Text.RegularExpressions;
using LocalProductManager.Api.Data;
using LocalProductManager.Api.Dtos;
using LocalProductManager.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LocalProductManager.Api.Services;

public sealed class AmazonAttributionImportService(AppDbContext db)
{
    private static readonly Regex UrlRegex = new(
        """https?://[^\s"',<>]+""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

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

        var imported = new List<AmazonAttributionLink>();
        var clickThroughUrl = ExtractClickThroughUrl(rows);
        if (!string.IsNullOrWhiteSpace(clickThroughUrl))
        {
            await UpsertAmazonListingAsync(productId, clickThroughUrl, cancellationToken);
        }

        var links = ExtractPlatformLinks(rows, csv);
        foreach (var (platform, url) in links)
        {
            var link = await UpsertLinkAsync(
                productId,
                amazonAsinId,
                platform,
                url,
                file.FileName,
                cancellationToken);

            imported.Add(link);
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

    private async Task UpsertAmazonListingAsync(
        int productId,
        string listingUrl,
        CancellationToken cancellationToken)
    {
        var listing = await db.ProductListings.FirstOrDefaultAsync(
            existing => existing.ProductId == productId && existing.Platform == "amazon",
            cancellationToken);
        if (listing is null)
        {
            db.ProductListings.Add(new ProductListing
            {
                ProductId = productId,
                Platform = "amazon",
                ListingUrl = listingUrl
            });
        }
        else
        {
            listing.ListingUrl = listingUrl;
            listing.UpdatedAt = DateTimeOffset.UtcNow;
        }
    }

    internal static string NormalizeHeader(string value) => value
        .Trim()
        .ToLowerInvariant()
        .Replace(" ", "")
        .Replace("-", "")
        .Replace("_", "");

    internal static bool IsClickThroughHeader(string header)
    {
        var normalized = NormalizeHeader(header);
        return normalized.Contains("clickthrough", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("clickthru", StringComparison.OrdinalIgnoreCase);
    }

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

    private static IReadOnlyList<(string Platform, string Url)> ExtractPlatformLinks(
        IReadOnlyList<List<string>> rows,
        string rawCsv)
    {
        var links = ExtractFromHeaderRows(rows);
        return links.Count > 0 ? links : ExtractFromLooseRows(rows, rawCsv);
    }

    internal static string? ExtractClickThroughUrl(IReadOnlyList<List<string>> rows)
    {
        for (var headerIndex = 0; headerIndex < Math.Min(rows.Count, 12); headerIndex++)
        {
            var header = rows[headerIndex];
            var clickThroughColumn = header.FindIndex(IsClickThroughHeader);
            if (clickThroughColumn < 0) continue;

            var candidates = rows
                .Skip(headerIndex + 1)
                .Where(row => clickThroughColumn < row.Count)
                .Select(row => CleanUrl(row[clickThroughColumn]))
                .Where(url => !string.IsNullOrWhiteSpace(url))
                .Cast<string>()
                .ToArray();

            return candidates.FirstOrDefault(IsAmazonUrl) ?? candidates.FirstOrDefault();
        }

        return null;
    }

    private static List<(string Platform, string Url)> ExtractFromHeaderRows(IReadOnlyList<List<string>> rows)
    {
        var imported = new List<(string Platform, string Url)>();
        if (rows.Count == 0) return imported;

        for (var headerIndex = 0; headerIndex < Math.Min(rows.Count, 12); headerIndex++)
        {
            var header = rows[headerIndex].Select(NormalizeHeader).ToArray();
            var publisherColumn = FindHeaderIndex(header, "publisher", "platform");
            if (publisherColumn < 0)
            {
                publisherColumn = FindHeaderIndex(header, "adgroup", "adgroupname");
            }
            var attributionColumn = FindHeaderIndex(header, "attributiontags", "attributiontag", "attributionlink", "attributionurl");
            if (publisherColumn >= 0 && attributionColumn >= 0)
            {
                foreach (var row in rows.Skip(headerIndex + 1))
                {
                    if (publisherColumn >= row.Count || attributionColumn >= row.Count) continue;
                    var platform = PlatformFromText(row[publisherColumn]);
                    var url = CleanUrl(row[attributionColumn]);
                    if (platform is not null && !string.IsNullOrWhiteSpace(url))
                    {
                        imported.Add((platform, url));
                    }
                }

                if (imported.Count > 0) return Deduplicate(imported);
            }

            var platformColumns = header
                .Select((value, index) => new { Platform = PlatformFromHeader(value), Index = index })
                .Where(item => item.Platform is not null)
                .ToArray();
            if (platformColumns.Length == 0) continue;

            foreach (var row in rows.Skip(headerIndex + 1))
            {
                foreach (var column in platformColumns)
                {
                    if (column.Index >= row.Count) continue;
                    var url = CleanUrl(row[column.Index]);
                    if (string.IsNullOrWhiteSpace(url)) continue;
                    imported.Add((column.Platform!, url));
                }
            }

            if (imported.Count > 0) return Deduplicate(imported);
        }

        return imported;
    }

    private static int FindHeaderIndex(IReadOnlyList<string> header, params string[] names)
    {
        for (var index = 0; index < header.Count; index++)
        {
            if (names.Any(name => header[index].Contains(name, StringComparison.OrdinalIgnoreCase)))
            {
                return index;
            }
        }

        return -1;
    }

    private static List<(string Platform, string Url)> ExtractFromLooseRows(
        IReadOnlyList<List<string>> rows,
        string rawCsv)
    {
        var imported = new List<(string Platform, string Url)>();
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            for (var index = 0; index < row.Count; index++)
            {
                var url = CleanUrl(row[index]);
                if (string.IsNullOrWhiteSpace(url)) continue;

                var context = BuildLooseContext(rows, rowIndex, index);
                var platform = PlatformFromText(context) ?? PlatformFromUrl(url);
                if (platform is not null) imported.Add((platform, url));
            }
        }

        if (imported.Count > 0) return Deduplicate(imported);

        foreach (Match match in UrlRegex.Matches(rawCsv))
        {
            var url = CleanUrl(match.Value);
            if (string.IsNullOrWhiteSpace(url)) continue;
            var platform = PlatformFromUrl(url);
            if (platform is not null) imported.Add((platform, url));
        }

        return Deduplicate(imported);
    }

    private static string BuildLooseContext(
        IReadOnlyList<List<string>> rows,
        int rowIndex,
        int cellIndex)
    {
        var values = new List<string>();
        for (var scanRow = Math.Max(0, rowIndex - 2); scanRow <= Math.Min(rows.Count - 1, rowIndex + 2); scanRow++)
        {
            var row = rows[scanRow];
            if (scanRow == rowIndex)
            {
                values.AddRange(row.Skip(Math.Max(0, cellIndex - 3)).Take(7));
            }
            else
            {
                values.AddRange(row);
            }
        }

        return string.Join(" ", values);
    }

    private static string? PlatformFromText(string value)
    {
        var normalized = NormalizeHeader(value);
        foreach (var platform in AllowedValues.AttributionPlatforms)
        {
            if (normalized.Contains(platform, StringComparison.OrdinalIgnoreCase))
            {
                return platform;
            }
        }

        if (normalized.Contains("meta", StringComparison.OrdinalIgnoreCase)) return "facebook";
        if (normalized.Contains("fb", StringComparison.OrdinalIgnoreCase)) return "facebook";
        if (normalized.Contains("pin", StringComparison.OrdinalIgnoreCase)) return "pinterest";
        if (normalized.Contains("ig", StringComparison.OrdinalIgnoreCase)) return "instagram";
        if (normalized.Contains("ins", StringComparison.OrdinalIgnoreCase)) return "instagram";
        if (normalized.Contains("tt", StringComparison.OrdinalIgnoreCase)) return "tiktok";
        if (normalized.Contains("yt", StringComparison.OrdinalIgnoreCase)) return "youtube";
        return null;
    }

    private static string? PlatformFromUrl(string url)
    {
        var normalized = url.ToLowerInvariant();
        if (normalized.Contains("pinterest") || normalized.Contains("pin.it")) return "pinterest";
        if (normalized.Contains("facebook") || normalized.Contains("fb.") || normalized.Contains("meta")) return "facebook";
        if (normalized.Contains("instagram")) return "instagram";
        if (normalized.Contains("tiktok")) return "tiktok";
        if (normalized.Contains("youtube") || normalized.Contains("youtu.be")) return "youtube";
        if (normalized.Contains("shopify")) return "shopify";
        return null;
    }

    internal static string? CleanUrl(string value)
    {
        var match = UrlRegex.Match(value.Trim());
        if (!match.Success) return null;

        return match.Value
            .Trim()
            .TrimEnd('.', ',', ';', ')', ']', '}');
    }

    private static bool IsAmazonUrl(string url) =>
        url.Contains("amazon.", StringComparison.OrdinalIgnoreCase) ||
        url.Contains("amzn.", StringComparison.OrdinalIgnoreCase);

    private static List<(string Platform, string Url)> Deduplicate(
        IEnumerable<(string Platform, string Url)> links) => links
            .GroupBy(link => link.Platform, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(link => IsAttributionUrl(link.Url))
                .First())
            .ToList();

    private static bool IsAttributionUrl(string url) =>
        url.Contains("maas=", StringComparison.OrdinalIgnoreCase) ||
        url.Contains("tag=maas", StringComparison.OrdinalIgnoreCase) ||
        url.Contains("ref_=aa_maas", StringComparison.OrdinalIgnoreCase);

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
