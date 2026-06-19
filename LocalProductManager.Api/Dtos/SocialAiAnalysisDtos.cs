namespace LocalProductManager.Api.Dtos;

public sealed record SocialAiAnalysisStatusDto(
    bool IsConfigured,
    string Model,
    string ReportDirectory,
    string? Message);

public sealed record SocialAiAnalysisRunRequest(
    string Period,
    DateOnly? Date);

public sealed record SocialAiAnalysisResultDto(
    string Period,
    DateOnly From,
    DateOnly To,
    string Model,
    string Path,
    string Summary,
    DateTimeOffset GeneratedAt);

public sealed record SocialAiAnalysisReportDto(
    string FileName,
    string Path,
    string Period,
    DateOnly From,
    DateOnly To,
    DateTimeOffset GeneratedAt,
    string Preview);
