using System.Text.Json;
using LocalProductManager.Api.Dtos;
using LocalProductManager.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace LocalProductManager.Api.Controllers;

[ApiController]
[Route("api/exchange-rates")]
public sealed class ExchangeRatesController(ExchangeRateService exchangeRates) : ControllerBase
{
    [HttpGet("rmb-to-usd")]
    public async Task<ActionResult<RmbToUsdRateDto>> RmbToUsd(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await exchangeRates.GetRmbToUsdAsync(cancellationToken));
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or JsonException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, ex.Message);
        }
    }
}
