using LocalProductManager.Api.Data;
using LocalProductManager.Api.Dtos;
using LocalProductManager.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LocalProductManager.Api.Services;

public sealed class AttributionFolderImportService(
    AppDbContext db,
    AmazonAttributionImportService attributionImporter)
{
    public async Task<AttributionFolderImportResult> ImportAsync(
        IReadOnlyList<IFormFile> files,
        CancellationToken cancellationToken)
    {
        var products = await db.Products
            .AsNoTracking()
            .Where(product => product.Listings.Any(listing => listing.Platform == "amazon"))
            .Select(product => new ProductMatch(product.Id, product.Sku))
            .ToListAsync(cancellationToken);

        var candidates = products
            .OrderByDescending(product => product.Sku.Length)
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
        return candidates.FirstOrDefault(product =>
            title.Contains(product.Sku, StringComparison.OrdinalIgnoreCase));
    }

    private sealed record ProductMatch(int Id, string Sku);
}
