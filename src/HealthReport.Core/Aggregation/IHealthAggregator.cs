using HealthReport.Core.Models;

namespace HealthReport.Core.Aggregation;

public interface IHealthAggregator
{
    /// <summary>
    /// Genera un resumen de salud para los últimos <paramref name="days"/> días.
    /// </summary>
    HealthSummary Aggregate(
        UserProfile profile,
        IEnumerable<HealthRecord> records,
        IEnumerable<WorkoutRecord> workouts,
        int days = 90);

    /// <summary>
    /// Devuelve la serie temporal diaria de las métricas indicadas
    /// usando suma o media según el tipo de métrica.
    /// </summary>
    IReadOnlyList<MetricTimeSeries> GetTimeSeries(
        IEnumerable<HealthRecord> records,
        IEnumerable<string> metricTypes,
        int days = 90);
}
