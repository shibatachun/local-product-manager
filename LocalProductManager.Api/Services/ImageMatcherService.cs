using LocalProductManager.Api.Dtos;

namespace LocalProductManager.Api.Services;

public sealed class ImageMatcherService
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg",
        ".jpeg",
        ".png",
        ".webp",
        ".bmp",
        ".gif"
    };

    public MatchImagesResponse Match(MatchImagesRequest request)
    {
        var sku = request.Sku.Trim();
        var folderPath = request.FolderPath.Trim();
        var mode = request.Mode.Trim().ToLowerInvariant();

        if (mode is not ("folder" or "sku"))
        {
            throw new ArgumentException("Image match mode must be 'folder' or 'sku'.");
        }

        if (mode == "sku" && string.IsNullOrWhiteSpace(sku))
        {
            throw new ArgumentException("SKU is required for SKU image matching.");
        }

        if (!Directory.Exists(folderPath))
        {
            throw new DirectoryNotFoundException($"Folder not found: {folderPath}");
        }

        var matches = Directory
            .EnumerateFiles(folderPath)
            .Where(path => ImageExtensions.Contains(Path.GetExtension(path)))
            .Where(path =>
                mode == "folder" ||
                Path.GetFileNameWithoutExtension(path).Contains(sku, StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path)
            .ToArray();

        return new MatchImagesResponse(sku, folderPath, mode, matches);
    }
}
