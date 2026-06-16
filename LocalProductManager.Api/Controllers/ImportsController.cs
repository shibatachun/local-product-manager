using LocalProductManager.Api.Dtos;
using LocalProductManager.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace LocalProductManager.Api.Controllers;

[ApiController]
[Route("api/imports")]
public sealed class ImportsController(
    AmazonBusinessReportImportService businessReportImporter,
    BulkAttributionImportService attributionImporter,
    AttributionFolderImportService attributionFolderImporter) : ControllerBase
{
    [HttpPost("amazon-business-report")]
    [RequestSizeLimit(25_000_000)]
    public async Task<ActionResult<BusinessReportImportResult>> ImportBusinessReport(
        IFormFile file,
        CancellationToken cancellationToken)
    {
        if (file.Length == 0) return BadRequest("The Business Report file is empty.");
        try
        {
            return Ok(await businessReportImporter.ImportAsync(file, cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("amazon-attributions")]
    [RequestSizeLimit(25_000_000)]
    public async Task<ActionResult<BulkAttributionImportResult>> ImportAttributions(
        IFormFile file,
        CancellationToken cancellationToken)
    {
        if (file.Length == 0) return BadRequest("The attribution file is empty.");
        try
        {
            return Ok(await attributionImporter.ImportAsync(file, cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("amazon-attribution-files")]
    [RequestSizeLimit(250_000_000)]
    public async Task<ActionResult<AttributionFolderImportResult>> ImportAttributionFiles(
        [FromForm] List<IFormFile> files,
        CancellationToken cancellationToken)
    {
        if (files.Count == 0)
        {
            return BadRequest("Select a folder containing attribution CSV files.");
        }

        return Ok(await attributionFolderImporter.ImportAsync(files, cancellationToken));
    }
}
