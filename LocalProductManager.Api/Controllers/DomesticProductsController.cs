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
            var tokens = SearchTokens(keyword);
            var coreTokens = tokens.Where(token => !IsVariantDetailToken(token)).ToArray();
            if (coreTokens.Length == 0) coreTokens = tokens;

            // Build the candidate set in PostgreSQL. Union produces DISTINCT SQL,
            // so even broad searches never require loading the catalog into memory.
            IQueryable<DomesticProductInfo>? candidates = null;
            foreach (var token in tokens)
            {
                var tokenMatches = MatchingToken(token);
                candidates = candidates is null ? tokenMatches : candidates.Union(tokenMatches);
            }
            rows = candidates ?? rows.Where(_ => false);

            var allCoreMatches = db.DomesticProductInfos.AsNoTracking();
            foreach (var token in coreTokens) allCoreMatches = FilterByToken(allCoreMatches, token);
            var allTokenMatches = db.DomesticProductInfos.AsNoTracking();
            foreach (var token in tokens) allTokenMatches = FilterByToken(allTokenMatches, token);
            var allCoreSkuMatches = db.DomesticProductInfos.AsNoTracking();
            foreach (var token in coreTokens) allCoreSkuMatches = FilterSkuByToken(allCoreSkuMatches, token);
            var allTokenSkuMatches = db.DomesticProductInfos.AsNoTracking();
            foreach (var token in tokens) allTokenSkuMatches = FilterSkuByToken(allTokenSkuMatches, token);
            var coreAndSizeSkuMatches = allCoreSkuMatches;
            foreach (var token in tokens.Where(IsSizeToken).Select(VariantNumber))
                coreAndSizeSkuMatches = FilterSkuByToken(coreAndSizeSkuMatches, token);

            // Google-style relevance bands: exact SKU, full SKU prefix, every
            // token, every core token, then partial matches. SKU breaks ties.
            rows = rows
                .OrderByDescending(item => item.Sku.ToLower() == normalizedKeyword)
                .ThenByDescending(item => item.Sku.ToLower().StartsWith(normalizedKeyword))
                .ThenByDescending(item => allTokenSkuMatches.Select(match => match.Id).Contains(item.Id))
                .ThenByDescending(item => coreAndSizeSkuMatches.Select(match => match.Id).Contains(item.Id))
                .ThenByDescending(item => allCoreSkuMatches.Select(match => match.Id).Contains(item.Id))
                .ThenByDescending(item => allTokenMatches.Select(match => match.Id).Contains(item.Id))
                .ThenByDescending(item => allCoreMatches.Select(match => match.Id).Contains(item.Id));
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

    private IQueryable<DomesticProductInfo> MatchingToken(string token) =>
        FilterByToken(db.DomesticProductInfos.AsNoTracking(), token);

    private static IQueryable<DomesticProductInfo> FilterByToken(
        IQueryable<DomesticProductInfo> rows, string token) => rows.Where(item =>
            item.Sku.ToLower().Contains(token) ||
            (item.ProductCode != null && item.ProductCode.ToLower().Contains(token)) ||
            (item.ProductName != null && item.ProductName.ToLower().Contains(token)) ||
            (item.EnglishDeclarationName != null && item.EnglishDeclarationName.ToLower().Contains(token)) ||
            (item.ChineseDeclarationName != null && item.ChineseDeclarationName.ToLower().Contains(token)) ||
            (item.Material != null && item.Material.ToLower().Contains(token)) ||
            (item.SupplierName != null && item.SupplierName.ToLower().Contains(token)));

    private static IQueryable<DomesticProductInfo> FilterSkuByToken(
        IQueryable<DomesticProductInfo> rows, string token) => rows.Where(item =>
            item.Sku.ToLower().Contains(token) ||
            (item.ProductCode != null && item.ProductCode.ToLower().Contains(token)));

    private static string[] SearchTokens(string query) => Regex
        .Matches(query.ToLowerInvariant(), @"[\p{L}\p{N}]+")
        .Select(match => match.Value)
        .Where(token => token.Length >= 2 || Regex.IsMatch(token, @"[\u3400-\u9fff]"))
        .Distinct(StringComparer.Ordinal)
        .Take(8)
        .ToArray();

    private static bool IsVariantDetailToken(string token) => Regex.IsMatch(
        token,
        @"^\d+(?:\.\d+)?(?:pcs?|pieces?|mm|cm|m|g|kg|oz|lb|pack|set)$",
        RegexOptions.IgnoreCase);

    private static bool IsSizeToken(string token) => Regex.IsMatch(
        token, @"^\d+(?:\.\d+)?(?:mm|cm|m)$", RegexOptions.IgnoreCase);

    private static string VariantNumber(string token) => Regex.Match(token, @"^\d+(?:\.\d+)?").Value;

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
