namespace HealthReport.Core.Models;

public sealed class AnalysisResult
{
    public string PhaseName { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public bool IsSuccess { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTime CompletedAt { get; init; } = DateTime.UtcNow;
}
