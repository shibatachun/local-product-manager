using System.Text.Json;
using LocalProductManager.Api.Data;
using LocalProductManager.Api.Dtos;
using LocalProductManager.Api.Models;
using LocalProductManager.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace LocalProductManager.Api.Controllers;

[ApiController]
[Route("api/domestic-products")]
public sealed class DomesticProductsController(
    AppDbContext db, DomesticProductImportService importer, AuthService auth) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<DomesticProductSearchResult>> Search(
        [FromQuery] string? query, [FromQuery] string skuSort = "asc",
        [FromQuery] int page = 1, [FromQuery] int pageSize = 100,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 10, 100);
        var rows = db.DomesticProductInfos.AsNoTracking();
        var keyword = query?.Trim();
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var normalizedKeyword = keyword.ToLowerInvariant();
            var primaryToken = PrimarySearchToken(normalizedKeyword);
            if (!db.Database.IsNpgsql())
                return Problem("Domestic fuzzy search requires PostgreSQL pg_trgm.", statusCode: 501);

            rows = rows
                .Where(item => item.Sku.ToLower().StartsWith(primaryToken)
                    || item.ProductCode != null && item.ProductCode.ToLower().StartsWith(primaryToken)
                    || EF.Functions.TrigramsAreWordSimilar(
                        normalizedKeyword, EF.Property<string>(item, "SearchText")))
                .OrderByDescending(item => item.Sku.ToLower() == primaryToken
                    || item.ProductCode != null && item.ProductCode.ToLower() == primaryToken)
                .ThenByDescending(item => item.Sku.ToLower().StartsWith(primaryToken)
                    || item.ProductCode != null && item.ProductCode.ToLower().StartsWith(primaryToken))
                .ThenBy(item => EF.Functions.TrigramsWordSimilarityDistance(
                    normalizedKeyword, EF.Property<string>(item, "SearchText")));
        }

        var descending = skuSort.Equals("desc", StringComparison.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(keyword))
            rows = descending ? rows.OrderByDescending(item => item.Sku) : rows.OrderBy(item => item.Sku);
        else
            rows = descending ? ((IOrderedQueryable<DomesticProductInfo>)rows).ThenByDescending(item => item.Sku)
                : ((IOrderedQueryable<DomesticProductInfo>)rows).ThenBy(item => item.Sku);
        var total = await rows.CountAsync(cancellationToken);
        var items = await rows.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return Ok(new DomesticProductSearchResult(items.Select(ToDto).ToArray(), total, page, pageSize, descending ? "desc" : "asc"));
    }

    private static string PrimarySearchToken(string normalizedQuery)
    {
        var parts = Regex.Matches(normalizedQuery, @"[\p{L}\p{N}]+")
            .Select(match => match.Value)
            .ToArray();
        if (parts.Length == 0) return normalizedQuery;

        // Letter-only inventory families use the following numeric segment as
        // part of their identity: GR-236, EYE-029, CABOCHON-002. Numeric product
        // codes such as 55104 remain a single primary segment.
        return parts.Length > 1
            && Regex.IsMatch(parts[0], @"^\p{L}+$")
            && Regex.IsMatch(parts[1], @"^\d+$")
                ? $"{parts[0]}-{parts[1]}"
                : parts[0];
    }

    [HttpPost("import")]
    [RequestSizeLimit(100_000_000)]
    public async Task<ActionResult<DomesticProductImportResult>> Import(IFormFile file, CancellationToken cancellationToken)
    {
        if (!await auth.CanImportDataAsync(HttpContext, cancellationToken))
            return StatusCode(StatusCodes.Status403Forbidden, "Import permission is required.");
        if (file.Length == 0) return BadRequest("Choose a non-empty XLSX file.");
        if (!Path.GetExtension(file.FileName).Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
            return BadRequest("Only XLSX files are supported.");
        try { return Ok(await importer.ImportAsync(file, cancellationToken)); }
        catch (InvalidDataException exception) { return BadRequest(exception.Message); }
        catch (InvalidOperationException exception) { return BadRequest(exception.Message); }
    }

    private static DomesticProductDto ToDto(DomesticProductInfo item)
    {
        IReadOnlyDictionary<string, string> details;
        try { details = JsonSerializer.Deserialize<Dictionary<string, string>>(item.DetailsJson) ?? []; }
        catch (JsonException) { details = new Dictionary<string, string>(); }
        return new(item.Id, item.Sku, item.ProductCode, item.ProductName, item.Material, item.Specification,
            item.CostRmb, item.MinimumSalePriceUsd, item.WarehouseInventory, item.SupplierName,
            item.ChineseDeclarationName, item.EnglishDeclarationName, item.WeightGrams, item.ImagePath,
            details, item.SourceFileName, item.ImportedAt, item.UpdatedAt);
    }
}
