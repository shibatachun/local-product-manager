namespace LocalProductManager.Api.Dtos;

public sealed record LabelPrintingSettingsDto(
    bool Enabled,
    string Provider,
    string EndpointUrl,
    string TemplateName,
    string PrinterName,
    int TimeoutSeconds);

public sealed record SaveLabelPrintingSettingsRequest(
    bool Enabled,
    string? Provider,
    string? EndpointUrl,
    string? TemplateName,
    string? PrinterName,
    int TimeoutSeconds);

public sealed record PrintLabelItemRequest(
    string RecordType,
    string RecordId,
    string? Title,
    string? Sku,
    int Copies = 1);

public sealed record PrintLabelsRequest(IReadOnlyList<PrintLabelItemRequest> Items);

public sealed record PrintBidProductLabelRequest(int Copies = 1);

public sealed record LabelPrintResult(bool Accepted, string Message);
