namespace HealthReport.Core.Models;

/// <summary>
/// Estadísticas agregadas de una métrica concreta en un periodo de tiempo.
/// </summary>
public sealed class MetricSummary
{
    public string MetricType { get; init; } = string.Empty;
    public string Unit { get; init; } = string.Empty;
    public double Average { get; init; }
    public double Min { get; init; }
    public double Max { get; init; }
    public double Latest { get; init; }
    public int SampleCount { get; init; }
    /// <summary>Tendencia: positiva = subiendo, negativa = bajando (pendiente lineal simple).</summary>
    public double Trend { get; init; }
}

/// <summary>
/// Resumen completo de salud para un periodo, listo para enviar al modelo de IA.
/// </summary>
public sealed class HealthSummary
{
    public UserProfile Profile { get; init; } = new();
    public DateOnly PeriodStart { get; init; }
    public DateOnly PeriodEnd { get; init; }
    public int AnalysisDays { get; init; }
    public List<MetricSummary> Metrics { get; init; } = [];
    public List<WorkoutRecord> RecentWorkouts { get; init; } = [];
}
