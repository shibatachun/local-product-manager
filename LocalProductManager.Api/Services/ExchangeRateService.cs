using System.Text.Json;
using LocalProductManager.Api.Dtos;

namespace LocalProductManager.Api.Services;

public sealed class ExchangeRateService(HttpClient httpClient, IConfiguration configuration)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly SemaphoreSlim CacheLock = new(1, 1);
    private static DateOnly? cachedOn;
    private static RmbToUsdRateDto? cachedRate;

    public async Task<RmbToUsdRateDto> GetRmbToUsdAsync(CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (cachedOn == today && cachedRate is not null)
        {
            return cachedRate;
        }

        await CacheLock.WaitAsync(cancellationToken);
        try
        {
            // Another request may have populated the cache while this request waited.
            if (cachedOn == today && cachedRate is not null)
            {
                return cachedRate;
            }

            cachedRate = await FetchRmbToUsdAsync(cancellationToken);
            cachedOn = today;
            return cachedRate;
        }
        finally
        {
            CacheLock.Release();
        }
    }

    private async Task<RmbToUsdRateDto> FetchRmbToUsdAsync(CancellationToken cancellationToken)
    {
        var url = configuration["ExchangeRates:RmbToUsdUrl"] ??
            "https://api.frankfurter.dev/v2/rate/CNY/USD";
        using var response = await httpClient.GetAsync(url, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Exchange rate request failed: HTTP {(int)response.StatusCode}. {body}");
        }

        var rate = JsonSerializer.Deserialize<FrankfurterRateResponse>(body, JsonOptions) ??
            throw new InvalidOperationException("Exchange rate response was empty.");
        if (rate.Rate <= 0)
        {
            throw new InvalidOperationException("Exchange rate response did not include a valid CNY to USD rate.");
        }

        return new RmbToUsdRateDto(
            rate.Rate,
            rate.Date,
            "Frankfurter CNY/USD");
    }

    private sealed record FrankfurterRateResponse(
        decimal Rate,
        DateOnly Date);
}
