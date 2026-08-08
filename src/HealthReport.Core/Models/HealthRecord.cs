namespace HealthReport.Core.Models;

/// <summary>
/// Representa un registro individual de salud parseado desde export.xml.
/// </summary>
public sealed class HealthRecord
{
    public string Type { get; init; } = string.Empty;
    public string SourceName { get; init; } = string.Empty;
    public string Unit { get; init; } = string.Empty;
    public double Value { get; init; }
    public DateTime StartDate { get; init; }
    public DateTime EndDate { get; init; }
}
