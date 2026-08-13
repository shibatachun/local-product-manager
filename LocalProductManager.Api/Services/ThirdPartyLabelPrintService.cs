using System.Net.Http.Json;
using System.Text.Json;
using LocalProductManager.Api.Dtos;

namespace LocalProductManager.Api.Services;

public sealed class ThirdPartyLabelPrintService(IWebHostEnvironment environment, IHttpClientFactory httpClientFactory)
{
    private readonly string settingsPath = Path.Combine(environment.ContentRootPath, "Data", "label-printing.json");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<LabelPrintingSettingsDto> GetSettingsAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(settingsPath)) return Defaults();
        try
        {
            await using var stream = File.OpenRead(settingsPath);
            return await JsonSerializer.DeserializeAsync<LabelPrintingSettingsDto>(stream, JsonOptions, cancellationToken) ?? Defaults();
        }
        catch (JsonException) { return Defaults(); }
    }

    public async Task<LabelPrintingSettingsDto> SaveSettingsAsync(SaveLabelPrintingSettingsRequest request, CancellationToken cancellationToken)
    {
        var settings = new LabelPrintingSettingsDto(
            request.Enabled,
            string.IsNullOrWhiteSpace(request.Provider) ? "BarTender" : request.Provider.Trim(),
            request.EndpointUrl?.Trim() ?? "",
            request.TemplateName?.Trim() ?? "",
            request.PrinterName?.Trim() ?? "",
            Math.Clamp(request.TimeoutSeconds, 2, 120));
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        await using var stream = File.Create(settingsPath);
        await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken);
        return settings;
    }

    public async Task<LabelPrintResult> SendAsync(IReadOnlyList<PrintLabelItemRequest> items, bool test, CancellationToken cancellationToken)
    {
        var settings = await GetSettingsAsync(cancellationToken);
        if (!settings.Enabled && !test) return new(false, "Third-party label printing is disabled in General Settings.");
        if (!Uri.TryCreate(settings.EndpointUrl, UriKind.Absolute, out var endpoint) || endpoint.Scheme is not ("http" or "https"))
            return new(false, "Enter a valid HTTP or HTTPS printing endpoint in General Settings.");

        var payload = new
        {
            action = test ? "test" : "print",
            provider = settings.Provider,
            template = settings.TemplateName,
            printer = settings.PrinterName,
            recordType = items.FirstOrDefault()?.RecordType ?? "connection-test",
            recordId = items.FirstOrDefault()?.RecordId ?? "",
            copies = Math.Clamp(items.FirstOrDefault()?.Copies ?? 1, 1, 999),
            requestedAt = DateTimeOffset.UtcNow,
            items = items.Select(item => new { item.RecordType, item.RecordId, item.Title, item.Sku, copies = Math.Clamp(item.Copies, 1, 999) })
        };
        try
        {
            var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds);
            using var response = await client.PostAsJsonAsync(endpoint, payload, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return new(false, $"{settings.Provider} returned HTTP {(int)response.StatusCode}.");
            return new(true, test ? $"Connected to {settings.Provider}." : $"Print request accepted by {settings.Provider}.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new(false, $"Could not reach {settings.Provider}: {ex.Message}");
        }
    }

    private static LabelPrintingSettingsDto Defaults() => new(false, "BarTender", "http://localhost:5159", "", "", 15);
}
