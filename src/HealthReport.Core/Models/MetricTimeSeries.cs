namespace HealthReport.Core.Models;

/// <summary>
/// Un punto de datos diario para representar series temporales en gráficos.
/// </summary>
public sealed class DailyDataPoint
{
    public DateOnly Date { get; init; }
    public double Value { get; init; }
}

/// <summary>
/// Serie temporal de una métrica, lista para ser graficada.
/// </summary>
public sealed class MetricTimeSeries
{
    public string MetricType { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Unit { get; init; } = string.Empty;
    public List<DailyDataPoint> Points { get; init; } = [];
    public double Average => Points.Count > 0 ? Points.Average(p => p.Value) : 0;
}
