namespace LocalProductManager.Api.Dtos;

public sealed record BusinessReportImportResult(
    int RowsRead,
    int ParentProductsCreated,
    int ChildProductsCreated,
    int StandaloneProductsCreated,
    int ProductsUpdated,
    int VariationGroups);

public sealed record BulkAttributionImportResult(
    int RowsRead,
    int ProductsMatched,
    int LinksImported,
    IReadOnlyList<string> UnmatchedSkus);

public sealed record AttributionFolderImportResult(
    int FilesRead,
    int FilesMatched,
    int ProductsMatched,
    int LinksImported,
    IReadOnlyList<string> UnmatchedFiles);
