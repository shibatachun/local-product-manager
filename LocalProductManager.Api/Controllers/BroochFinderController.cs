using LocalProductManager.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace LocalProductManager.Api.Controllers;

[ApiController]
[Route("api/brooch-finder")]
public sealed class BroochFinderController(BroochFinderService finder) : ControllerBase
{
    private const long MaximumImageBytes = 15L * 1024 * 1024;

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken token) =>
        Ok((await finder.ListAsync(token)).Select(PublicRecord));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] BroochFindDetails request, CancellationToken token)
    {
        try { return Ok(PublicRecord(await finder.CreateAsync(request.Label, request.Location, token))); }
        catch (ArgumentException error) { return BadRequest(error.Message); }
    }

    [HttpPost("create-with-photo")]
    [RequestSizeLimit(MaximumImageBytes + 1024)]
    public async Task<IActionResult> CreateWithPhoto([FromQuery] string? label, [FromQuery] string? location, CancellationToken token)
    {
        try { return Ok(PublicRecord(await finder.CreateWithPhotoAsync(label, location, await ImageBytes(token), token))); }
        catch (ArgumentException error) { return BadRequest(error.Message); }
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] BroochFindDetails request, CancellationToken token)
    {
        try
        {
            var record = await finder.UpdateAsync(id, request.Label, request.Location, token);
            return record is null ? NotFound() : Ok(PublicRecord(record));
        }
        catch (ArgumentException error) { return BadRequest(error.Message); }
    }

    [HttpPost("{id:guid}/photos")]
    [RequestSizeLimit(MaximumImageBytes + 1024)]
    public async Task<IActionResult> AddPhoto(Guid id, CancellationToken token)
    {
        try
        {
            var record = await finder.AddPhotoAsync(id, await ImageBytes(token), token);
            return record is null ? NotFound() : Ok(PublicRecord(record));
        }
        catch (ArgumentException error) { return BadRequest(error.Message); }
    }

    [HttpGet("{id:guid}/photos/{photoId:guid}")]
    public async Task<IActionResult> Photo(Guid id, Guid photoId, CancellationToken token)
    {
        var path = await finder.PhotoPathAsync(id, photoId, token);
        return path is null ? NotFound() : PhysicalFile(path, "image/jpeg");
    }

    [HttpPost("search")]
    [RequestSizeLimit(MaximumImageBytes + 1024)]
    public async Task<IActionResult> Search(CancellationToken token)
    {
        try
        {
            var matches = await finder.SearchAsync(await ImageBytes(token), token);
            return Ok(matches.Select(match => new
            {
                record = PublicRecord(match.Record), match.BestPhotoId, match.VisualScore
            }));
        }
        catch (ArgumentException error) { return BadRequest(error.Message); }
    }

    private async Task<byte[]> ImageBytes(CancellationToken token)
    {
        var contentType = Request.ContentType?.Split(';', 2)[0].ToLowerInvariant();
        if (contentType is not ("image/jpeg" or "image/png" or "image/webp" or "image/bmp" or "image/gif"))
            throw new ArgumentException("Choose a JPG, PNG, WebP, BMP, or GIF image.");
        if (Request.ContentLength is <= 0 or > MaximumImageBytes)
            throw new ArgumentException("Choose an image between 1 byte and 15 MB.");
        await using var buffer = new MemoryStream();
        await Request.Body.CopyToAsync(buffer, token);
        if (buffer.Length is 0 or > MaximumImageBytes)
            throw new ArgumentException("Choose an image between 1 byte and 15 MB.");
        var expected = Request.Headers["X-Upload-Expected-Bytes"].FirstOrDefault();
        if (long.TryParse(expected, out var size) && size != buffer.Length)
            throw new ArgumentException("The selected image was not fully uploaded. Please choose it again.");
        return buffer.ToArray();
    }

    private static object PublicRecord(BroochFindRecord item) => new
    {
        item.Id, item.Label, item.Location, item.UpdatedAt,
        photos = item.Photos.Select(photo => new { photo.Id })
    };
}

public sealed record BroochFindDetails(string? Label, string? Location);
