namespace LocalProductManager.Api.Dtos;

public sealed record RmbToUsdRateDto(
    decimal Rate,
    DateOnly Date,
    string Source);
