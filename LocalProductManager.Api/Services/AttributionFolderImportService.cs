using LocalProductManager.Api.Data;
using LocalProductManager.Api.Dtos;
using LocalProductManager.Api.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace LocalProductManager.Api.Services;

public sealed class AttributionFolderImportService(
    AppDbContext db,
    AmazonAttributionImportService attributionImporter)
{
    private static readonly Regex FiveDigitTokenRegex = new(@"\d{5}", RegexOptions.Compiled);

    public async Task<AttributionFolderImportResult> ImportAsync(
        IReadOnlyList<IFormFile> files,
        CancellationToken cancellationToken)
    {
        var products = await db.AmazonMarketingProducts
            .AsNoTracking()
            .Select(product => new ProductMatch(product.Id, product.Sku, product.ProductType, product.ParentProductId, null))
            .ToListAsync(cancellationToken);

        var candidates = products
            .Select(product => product with { FiveDigitToken = ExtractFiveDigitToken(product.Sku) })
            .OrderByDescending(product => product.Score)
            .ThenBy(product => product.Sku.Length)
            .ToArray();
        var matchedFiles = 0;
        var linksImported = 0;
        var matchedProductIds = new HashSet<int>();
        var unmatchedFiles = new List<string>();

        foreach (var file in files.Where(file => file.Length > 0))
        {
            var product = MatchProduct(file.FileName, candidates);
            if (product is null)
            {
                unmatchedFiles.Add(file.FileName);
                continue;
            }

            var result = await attributionImporter.ImportCsvAsync(
                product.Id,
                file,
                amazonAsinId: null,
                cancellationToken);
            if (result is null)
            {
                unmatchedFiles.Add(file.FileName);
                continue;
            }

            matchedFiles++;
            matchedProductIds.Add(product.Id);
            linksImported += result.ImportedCount;
        }

        return new AttributionFolderImportResult(
            files.Count,
            matchedFiles,
            matchedProductIds.Count,
            linksImported,
            unmatchedFiles);
    }

    private static ProductMatch? MatchProduct(
        string fileName,
        IReadOnlyList<ProductMatch> candidates)
    {
        var title = Path.GetFileNameWithoutExtension(fileName);
        var fileToken = ExtractFiveDigitToken(title);
        if (fileToken is not null)
        {
            var tokenMatch = candidates
                .Where(product => product.FiveDigitToken == fileToken)
                .OrderByDescending(product => product.Score)
                .ThenBy(product => product.Sku.Length)
                .FirstOrDefault();
            if (tokenMatch is not null) return tokenMatch;
        }

        return candidates.FirstOrDefault(product => title.Contains(product.Sku, StringComparison.OrdinalIgnoreCase));
    }

    private static string? ExtractFiveDigitToken(string value)
    {
        var match = FiveDigitTokenRegex.Match(value);
        return match.Success ? match.Value : null;
    }

    private sealed record ProductMatch(
        int Id,
        string Sku,
        string ProductType,
        int? ParentProductId,
        string? FiveDigitToken)
    {
        public int Score =>
            (ParentProductId is null ? 100 : 0) +
            (string.Equals(ProductType, "parent", StringComparison.OrdinalIgnoreCase) ? 50 : 0);
    }
}
