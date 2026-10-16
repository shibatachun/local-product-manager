using LocalProductManager.Api.Data;
using LocalProductManager.Api.Dtos;
using LocalProductManager.Api.Models;
using LocalProductManager.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LocalProductManager.Api.Controllers;

[ApiController]
[Route("api/live-stream-bid-products/show-attachments")]
public sealed class LiveStreamBidShowAttachmentsController(
    AppDbContext db, AuthService auth, IWebHostEnvironment environment) : ControllerBase
{
    private const long MaxFileBytes = 20L * 1024 * 1024;
    private static readonly Dictionary<string, string> ImageTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".png"] = "image/png",
        [".webp"] = "image/webp", [".heic"] = "image/heic", [".heif"] = "image/heif"
    };
    private static readonly Dictionary<string, string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] = ".jpg", ["image/png"] = ".png", ["image/webp"] = ".webp",
        ["image/heic"] = ".heic", ["image/heif"] = ".heif"
    };

    private string Root => Path.Combine(environment.ContentRootPath, "Data", "live-stream-bid-show-attachments");

    [HttpPost("orders/{orderId:int}/price-evidence")]
    [RequestSizeLimit(MaxFileBytes + 1024 * 1024)]
    public async Task<ActionResult<LiveStreamBidShowAttachmentDto>> UploadPriceEvidence(
        int orderId, CancellationToken cancellationToken)
    {
        if (!await CanAccessAsync(cancellationToken)) return Unauthorized();
        var order = await db.LiveStreamBidShowOrders.AsNoTracking()
            .Where(item => item.Id == orderId)
            .Select(item => new { item.ShowSessionId, item.CustomerName })
            .FirstOrDefaultAsync(cancellationToken);
        if (order is null) return NotFound();
        if (!Request.HasFormContentType) return BadRequest("Choose a photo to upload.");
        IFormCollection form;
        try { form = await Request.ReadFormAsync(cancellationToken); }
        catch (InvalidDataException) { return BadRequest("Could not read the photo upload. Please choose the photo again."); }
        var file = form.Files.GetFile("file");
        if (file is null || form.Files.Count != 1) return BadRequest("Choose one photo to upload.");
        return await SaveAsync(order.ShowSessionId, orderId, order.CustomerName, "price-evidence", file, false, cancellationToken);
    }

    [HttpPost("orders/{orderId:int}/price-evidence/raw")]
    [RequestSizeLimit(MaxFileBytes + 1024 * 1024)]
    public async Task<ActionResult<LiveStreamBidShowAttachmentDto>> UploadPriceEvidenceRaw(
        int orderId, CancellationToken cancellationToken)
    {
        if (!await CanAccessAsync(cancellationToken)) return Unauthorized();
        var order = await db.LiveStreamBidShowOrders.AsNoTracking()
            .Where(item => item.Id == orderId)
            .Select(item => new { item.ShowSessionId, item.CustomerName })
            .FirstOrDefaultAsync(cancellationToken);
        if (order is null) return NotFound();
        var contentType = Request.ContentType?.Split(';', 2)[0].Trim() ?? "";
        if (!ImageExtensions.TryGetValue(contentType, out var extension))
            return BadRequest("Choose a JPG, PNG, WebP, HEIC, or HEIF photo.");
        if (Request.ContentLength == 0)
            return BadRequest("The server received an empty photo upload. Try taking the photo again or choose one from the photo library.");
        if (Request.ContentLength > MaxFileBytes)
            return BadRequest("Choose a photo no larger than 20 MB.");

        await using var contents = new MemoryStream();
        await Request.Body.CopyToAsync(contents, cancellationToken);
        if (contents.Length == 0)
            return BadRequest("The server received an empty photo upload. Try taking the photo again or choose one from the photo library.");
        if (contents.Length > MaxFileBytes)
            return BadRequest("Choose a photo no larger than 20 MB.");
        contents.Position = 0;
        var file = new FormFile(contents, 0, contents.Length, "file", $"price-evidence-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}{extension}")
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };
        return await SaveAsync(order.ShowSessionId, orderId, order.CustomerName, "price-evidence", file, false, cancellationToken);
    }

    [HttpPost("shows/{showId:int}/shipment-documents")]
    [RequestSizeLimit(MaxFileBytes + 1024 * 1024)]
    public async Task<ActionResult<LiveStreamBidShowAttachmentDto>> UploadShipmentDocument(
        int showId, [FromForm] string customerName, [FromForm] string kind,
        [FromForm] IFormFile file, CancellationToken cancellationToken)
    {
        if (!await CanAccessAsync(cancellationToken)) return Unauthorized();
        if (kind is not ("shipping-slip" or "shipping-label"))
            return BadRequest("Choose a shipping slip or shipping label.");
        if (string.IsNullOrWhiteSpace(customerName)) return BadRequest("Choose a buyer.");
        var normalizedName = customerName.Trim().ToLowerInvariant();
        var buyer = await db.LiveStreamBidShowOrders.AsNoTracking()
            .Where(item => item.ShowSessionId == showId && item.CustomerName.ToLower() == normalizedName)
            .Select(item => item.CustomerName)
            .FirstOrDefaultAsync(cancellationToken);
        if (buyer is null) return NotFound("This buyer was not found in the show.");
        return await SaveAsync(showId, null, buyer, kind, file, true, cancellationToken);
    }

    [HttpGet("files/{id:int}")]
    public async Task<IActionResult> FileById(int id, CancellationToken cancellationToken)
    {
        if (!await CanAccessAsync(cancellationToken)) return Unauthorized();
        var attachment = await db.LiveStreamBidShowAttachments.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (attachment is null) return NotFound();
        var path = Path.Combine(Root, attachment.ShowSessionId.ToString(), attachment.StoredFileName);
        if (!System.IO.File.Exists(path)) return NotFound("The saved attachment file is missing.");
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers.XContentTypeOptions = "nosniff";
        return PhysicalFile(path, attachment.ContentType, enableRangeProcessing: true);
    }

    private async Task<ActionResult<LiveStreamBidShowAttachmentDto>> SaveAsync(
        int showId, int? orderId, string customerName, string kind,
        IFormFile file, bool allowPdf, CancellationToken cancellationToken)
    {
        if (file.Length is <= 0 or > MaxFileBytes) return BadRequest("Choose a file no larger than 20 MB.");
        var extension = Path.GetExtension(file.FileName);
        var isPdf = allowPdf && extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase);
        if (!isPdf && !ImageTypes.TryGetValue(extension, out _))
            return BadRequest("Choose a JPG, PNG, WebP, HEIC, or HEIF photo, or a PDF for shipment documents.");
        if (isPdf && !file.ContentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase))
            return BadRequest("The selected file is not a PDF.");
        if (!isPdf && !file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            return BadRequest("The selected file is not an image.");

        var contentType = isPdf ? "application/pdf" : ImageTypes[extension];
        var storedFileName = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        var folder = Path.Combine(Root, showId.ToString());
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, storedFileName);
        try
        {
            await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true))
                await file.CopyToAsync(output, cancellationToken);
            var user = await auth.GetOrCreateCurrentUserAsync(HttpContext, cancellationToken);
            var attachment = new LiveStreamBidShowAttachment
            {
                ShowSessionId = showId, ShowOrderId = orderId, CustomerName = customerName,
                Kind = kind, StoredFileName = storedFileName,
                OriginalFileName = Path.GetFileName(file.FileName)[..Math.Min(Path.GetFileName(file.FileName).Length, 260)],
                ContentType = contentType, UploadedBy = user.UserName
            };
            db.LiveStreamBidShowAttachments.Add(attachment);
            await db.SaveChangesAsync(cancellationToken);
            return Ok(ToDto(attachment));
        }
        catch
        {
            if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
            throw;
        }
    }

    private async Task<bool> CanAccessAsync(CancellationToken cancellationToken)
    {
        var user = await auth.GetOrCreateCurrentUserAsync(HttpContext, cancellationToken);
        return user.IsHost || user.IsAdmin || user.UserName != "guest";
    }

    private static LiveStreamBidShowAttachmentDto ToDto(LiveStreamBidShowAttachment item) =>
        new(item.Id, item.Kind, item.OriginalFileName, item.ContentType, item.UploadedAt, item.UploadedBy);
}
