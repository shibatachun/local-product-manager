using LocalProductManager.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace LocalProductManager.Api.Services;

public sealed class ProductImageFolderService(
    AppDbContext db,
    ImageFileService imageFiles,
    IWebHostEnvironment environment,
    ILogger<ProductImageFolderService> logger)
{
    public string GetProductFolder(int productId, string? localInventoryId)
    {
        var inventoryNumber = string.IsNullOrWhiteSpace(localInventoryId)
            ? $"INV-{productId}"
            : localInventoryId.Trim();
        var safeFolderName = string.Concat(inventoryNumber.Select(character =>
            Path.GetInvalidFileNameChars().Contains(character) ? '_' : character)).Trim(' ', '.');
        if (string.IsNullOrWhiteSpace(safeFolderName))
        {
            safeFolderName = $"INV-{productId}";
        }

        var root = Path.Combine(environment.ContentRootPath, "Data", "product-images");
        Directory.CreateDirectory(root);
        imageFiles.AddRoot(root);

        var folder = Path.Combine(root, safeFolderName);
        Directory.CreateDirectory(folder);
        return folder;
    }

    public async Task OrganizeExistingProductsAsync(CancellationToken cancellationToken = default)
    {
        var products = await db.Products.ToListAsync(cancellationToken);
        var copiedFiles = new List<(string Source, string Destination)>();

        foreach (var product in products)
        {
            var folder = GetProductFolder(product.Id, product.LocalInventoryId);
            if (product.ImageSource == "remote" ||
                string.IsNullOrWhiteSpace(product.ImagePath) ||
                Uri.TryCreate(product.ImagePath, UriKind.Absolute, out var imageUri) &&
                (imageUri.Scheme == Uri.UriSchemeHttp || imageUri.Scheme == Uri.UriSchemeHttps))
            {
                continue;
            }

            var source = Path.IsPathRooted(product.ImagePath)
                ? Path.GetFullPath(product.ImagePath)
                : Path.GetFullPath(Path.Combine(environment.ContentRootPath, product.ImagePath));
            if (!File.Exists(source) || IsDirectlyInside(source, folder))
            {
                continue;
            }

            var destination = GetAvailableDestination(folder, Path.GetFileName(source));
            File.Copy(source, destination, overwrite: false);
            product.ImagePath = destination;
            copiedFiles.Add((source, destination));
        }

        if (copiedFiles.Count == 0)
        {
            logger.LogInformation("Ensured LPM image folders for {ProductCount} products.", products.Count);
            return;
        }

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Created LPM image folders and copied {ImageCount} existing product images. Original files were retained.",
            copiedFiles.Count);
    }

    private static bool IsDirectlyInside(string filePath, string folder) =>
        string.Equals(
            Path.GetDirectoryName(filePath)?.TrimEnd(Path.DirectorySeparatorChar),
            folder.TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    private static string GetAvailableDestination(string folder, string fileName)
    {
        var destination = Path.Combine(folder, fileName);
        if (!File.Exists(destination))
        {
            return destination;
        }

        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        return Path.Combine(folder, $"{stem}-{Guid.NewGuid():N}{extension}");
    }
}
