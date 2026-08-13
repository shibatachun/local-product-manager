using LocalProductManager.Api.Data;
using LocalProductManager.Api.Dtos;
using LocalProductManager.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LocalProductManager.Api.Controllers;

[ApiController]
[Route("api/label-printing")]
public sealed class LabelPrintingController(ThirdPartyLabelPrintService printing, AppDbContext db, AuthService auth) : ControllerBase
{
    [HttpGet("settings")]
    public async Task<ActionResult<LabelPrintingSettingsDto>> GetSettings(CancellationToken cancellationToken) =>
        Ok(await printing.GetSettingsAsync(cancellationToken));

    [HttpPut("settings")]
    public async Task<ActionResult<LabelPrintingSettingsDto>> SaveSettings(SaveLabelPrintingSettingsRequest request, CancellationToken cancellationToken)
    {
        if (!await auth.IsAdminAsync(HttpContext, cancellationToken)) return Forbid();
        return Ok(await printing.SaveSettingsAsync(request, cancellationToken));
    }

    [HttpPost("test")]
    public async Task<ActionResult<LabelPrintResult>> Test(CancellationToken cancellationToken)
    {
        if (!await auth.IsAdminAsync(HttpContext, cancellationToken)) return Forbid();
        var result = await printing.SendAsync([], true, cancellationToken);
        return result.Accepted ? Ok(result) : StatusCode(StatusCodes.Status502BadGateway, result);
    }

    [HttpPost("print")]
    public async Task<ActionResult<LabelPrintResult>> Print(PrintLabelsRequest request, CancellationToken cancellationToken)
    {
        if (request.Items.Count is < 1 or > 1000) return BadRequest("Submit between 1 and 1,000 labels.");
        var result = await printing.SendAsync(request.Items, false, cancellationToken);
        return result.Accepted ? Ok(result) : StatusCode(StatusCodes.Status502BadGateway, result);
    }

    [HttpPost("bid-products/{id:int}")]
    public async Task<ActionResult<LabelPrintResult>> PrintBidProduct(int id, PrintBidProductLabelRequest request, CancellationToken cancellationToken)
    {
        if (request.Copies is < 1 or > 999) return BadRequest("Copies must be between 1 and 999.");
        var product = await db.LiveStreamBidProducts.AsNoTracking().FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (product is null) return NotFound();
        if (!product.RequiresLocalLabel) return Conflict("This externally imported product was already labeled before import.");
        var item = new PrintLabelItemRequest("bid-product", $"BID|{product.Uuid}", product.Title, product.ActualSku, request.Copies);
        var result = await printing.SendAsync([item], false, cancellationToken);
        return result.Accepted ? Ok(result) : StatusCode(StatusCodes.Status502BadGateway, result);
    }
}
