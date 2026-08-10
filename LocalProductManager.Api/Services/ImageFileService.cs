using System.Text.Json;
using Microsoft.AspNetCore.StaticFiles;

namespace LocalProductManager.Api.Services;

public sealed record ImageFileResult(
    string FullPath,
    string FileName,
    string ContentType);

public sealed record ImageStorageRoot(
    string Path,
    bool Available);

public sealed record ImageHostEntry(
    string Name,
    string Path,
    string Type,
    long? Size,
    DateTimeOffset? ModifiedAt,
    string? Extension = null);

public sealed record ImageHostBrowseResult(
    string Path,
    string? ParentPath,
    IReadOnlyList<ImageHostEntry> Folders,
    IReadOnlyList<ImageHostEntry> Files,
    IReadOnlyList<ImageHostEntry> Images);

public sealed class ImageFileService
{
    private readonly object sync = new();
    private readonly string contentRootPath;
    private readonly string storageFilePath;
    private List<string> allowedRoots;

    public ImageFileService(
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        contentRootPath = environment.ContentRootPath;
        storageFilePath = Path.Combine(contentRootPath, "Data", "image-storage-roots.json");

        var configuredRoots = configuration
            .GetSection("ImageStorage:AllowedRoots")
            .Get<string[]>() ?? [];

        allowedRoots = LoadStoredRoots(configuredRoots);
    }

    public IReadOnlyList<ImageStorageRoot> GetRoots()
    {
        string[] roots;
        lock (sync)
        {
            roots = allowedRoots.ToArray();
        }

        // Directory.Exists can take tens of seconds for an offline UNC share.
        // Never hold the roots lock during network I/O because product image
        // requests only need a quick snapshot of the allowed paths.
        return roots
            .Select(path => new ImageStorageRoot(path, Directory.Exists(path)))
            .ToArray();
    }

    public ImageStorageRoot AddRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("File storage path is required.");
        }

        var normalizedPath = NormalizePath(path, contentRootPath);
        lock (sync)
        {
            if (!allowedRoots.Contains(normalizedPath, StringComparer.OrdinalIgnoreCase))
            {
                allowedRoots.Add(normalizedPath);
                allowedRoots = allowedRoots
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(item => item)
                    .ToList();
                SaveRoots();
            }
        }

        return new ImageStorageRoot(normalizedPath, Directory.Exists(normalizedPath));
    }

    public bool RemoveRoot(string path)
    {
        var normalizedPath = NormalizePath(path, contentRootPath);
        lock (sync)
        {
            var removed = allowedRoots.RemoveAll(
                root => string.Equals(root, normalizedPath, StringComparison.OrdinalIgnoreCase)) > 0;
            if (removed)
            {
                SaveRoots();
            }

            return removed;
        }
    }

    public ImageFileResult GetImage(string imagePath)
    {
        var roots = GetRootPaths();
        if (roots.Length == 0)
        {
            throw new InvalidOperationException("No file storage roots are configured.");
        }

        var fullPath = NormalizePath(imagePath, contentRootPath);
        if (!roots.Any(root => IsUnderRoot(fullPath, root)))
        {
            throw new UnauthorizedAccessException("The file path is outside the configured file storage roots.");
        }

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("The file was not found.", fullPath);
        }

        var contentTypeProvider = new FileExtensionContentTypeProvider();
        if (!contentTypeProvider.TryGetContentType(fullPath, out var contentType))
        {
            contentType = "application/octet-stream";
        }

        return new ImageFileResult(fullPath, Path.GetFileName(fullPath), contentType);
    }

    public bool TryGetAllowedDirectory(string folderPath, out string fullPath)
    {
        var normalizedPath = NormalizePath(folderPath, contentRootPath);
        fullPath = normalizedPath;
        return GetRootPaths().Any(root => IsUnderRoot(normalizedPath, root));
    }

    public ImageHostBrowseResult BrowseDirectory(string folderPath)
    {
        if (!TryGetAllowedDirectory(folderPath, out var fullPath))
        {
            throw new UnauthorizedAccessException("The file host folder is outside the configured file storage roots.");
        }

        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException($"Folder not found: {fullPath}");
        }

        var roots = GetRootPaths();
        var parentPath = Directory.GetParent(fullPath)?.FullName;
        if (parentPath is null || !roots.Any(root => IsUnderRoot(parentPath, root)))
        {
            parentPath = null;
        }

        var folders = Directory
            .EnumerateDirectories(fullPath)
            .Select(path =>
            {
                var info = new DirectoryInfo(path);
                return new ImageHostEntry(info.Name, info.FullName, "folder", null, info.LastWriteTime);
            })
            .OrderBy(item => item.Name)
            .ToArray();

        var files = Directory
            .EnumerateFiles(fullPath)
            .Select(path =>
            {
                var info = new FileInfo(path);
                return new ImageHostEntry(
                    info.Name,
                    info.FullName,
                    "file",
                    info.Length,
                    info.LastWriteTime,
                    info.Extension);
            })
            .OrderBy(item => item.Name)
            .ToArray();
        var images = files
            .Where(file => IsImageExtension(file.Extension))
            .Select(file => file with { Type = "image" })
            .ToArray();

        return new ImageHostBrowseResult(fullPath, parentPath, folders, files, images);
    }

    private static bool IsImageExtension(string? extension) =>
        extension is not null &&
        (string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, ".webp", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, ".bmp", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, ".gif", StringComparison.OrdinalIgnoreCase));

    private List<string> LoadStoredRoots(IEnumerable<string> configuredRoots)
    {
        try
        {
            if (File.Exists(storageFilePath))
            {
                var json = File.ReadAllText(storageFilePath);
                var storedRoots = JsonSerializer.Deserialize<string[]>(json) ?? [];
                return NormalizeRoots(storedRoots);
            }
        }
        catch (JsonException)
        {
            // Fall back to appsettings when the runtime file is invalid.
        }

        var roots = NormalizeRoots(configuredRoots);
        Directory.CreateDirectory(Path.GetDirectoryName(storageFilePath)!);
        File.WriteAllText(storageFilePath, JsonSerializer.Serialize(roots, JsonOptions));
        return roots;
    }

    private List<string> NormalizeRoots(IEnumerable<string> roots) => roots
        .Where(root => !string.IsNullOrWhiteSpace(root))
        .Select(root => NormalizePath(root, contentRootPath))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(root => root)
        .ToList();

    private string[] GetRootPaths()
    {
        lock (sync)
        {
            return allowedRoots.ToArray();
        }
    }

    private void SaveRoots()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(storageFilePath)!);
        File.WriteAllText(
            storageFilePath,
            JsonSerializer.Serialize(allowedRoots, JsonOptions));
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static string NormalizePath(string path, string contentRootPath)
    {
        var trimmed = path.Trim();
        var fullPath = Path.IsPathRooted(trimmed)
            ? Path.GetFullPath(trimmed)
            : Path.GetFullPath(Path.Combine(contentRootPath, trimmed));
        return Path.TrimEndingDirectorySeparator(fullPath);
    }

    private static bool IsUnderRoot(string path, string root)
    {
        if (string.Equals(path, root, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return path.StartsWith(
            root + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
    }
}
