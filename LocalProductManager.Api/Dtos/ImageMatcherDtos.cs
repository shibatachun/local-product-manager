namespace LocalProductManager.Api.Dtos;

public sealed record MatchImagesRequest(
    string Sku,
    string FolderPath,
    string Mode = "sku");

public sealed record MatchImagesResponse(
    string Sku,
    string FolderPath,
    string Mode,
    IReadOnlyList<string> MatchedImages);
