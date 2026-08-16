using HealthReport.Core.Models;

namespace HealthReport.Core.Aggregation;

/// <summary>
/// Agrega los registros de salud en resúmenes estadísticos compactos aptos para el modelo de IA.
/// </summary>
public sealed class HealthAggregator : IHealthAggregator
{
    private const string SleepMetricType = "HKCategoryTypeIdentifierSleepAnalysis";

    // Métricas que se incluyen en el análisis, en orden de prioridad.
    private static readonly string[] RelevantMetrics =
    [
        "HKQuantityTypeIdentifierStepCount",
        "HKQuantityTypeIdentifierHeartRate",
        "HKQuantityTypeIdentifierRestingHeartRate",
        "HKQuantityTypeIdentifierHeartRateVariabilitySDNN",
        "HKQuantityTypeIdentifierVO2Max",
        "HKQuantityTypeIdentifierActiveEnergyBurned",
        "HKQuantityTypeIdentifierBasalEnergyBurned",
        "HKQuantityTypeIdentifierDistanceWalkingRunning",
        "HKQuantityTypeIdentifierOxygenSaturation",
        "HKQuantityTypeIdentifierRespiratoryRate",
        "HKQuantityTypeIdentifierBodyMass",
        "HKCategoryTypeIdentifierSleepAnalysis",
        "HKQuantityTypeIdentifierAppleSleepingBreathingDisturbances",
        "HKQuantityTypeIdentifierWalkingSpeed",
        "HKQuantityTypeIdentifierWalkingStepLength",
        "HKQuantityTypeIdentifierWalkingAsymmetryPercentage",
        "HKQuantityTypeIdentifierWalkingSteadiness"
    ];

    private static readonly HashSet<string> CumulativeMetrics =
    [
        "HKQuantityTypeIdentifierStepCount",
        "HKQuantityTypeIdentifierActiveEnergyBurned",
        "HKQuantityTypeIdentifierBasalEnergyBurned",
        "HKQuantityTypeIdentifierDistanceWalkingRunning"
    ];

    private static readonly HashSet<string> AsleepValues =
    [
        "1",
        "HKCategoryValueSleepAnalysisAsleep",
        "HKCategoryValueSleepAnalysisAsleepCore",
        "HKCategoryValueSleepAnalysisAsleepDeep",
        "HKCategoryValueSleepAnalysisAsleepREM",
        "HKCategoryValueSleepAnalysisAsleepUnspecified"
    ];

    public HealthSummary Aggregate(
        UserProfile profile,
        IEnumerable<HealthRecord> records,
        IEnumerable<WorkoutRecord> workouts,
        int days = 90)
    {
        var cutoff = DateTime.Now.AddDays(-days).Date;
        var recentRecords = records
            .Where(r => r.StartDate >= cutoff)
            .ToList();

        var metrics = RelevantMetrics
            .Select(metricType => ComputeMetric(metricType, recentRecords))
            .Where(m => m is not null)
            .Cast<MetricSummary>()
            .ToList();

        var recentWorkouts = workouts
            .Where(w => w.StartDate >= cutoff)
            .OrderByDescending(w => w.StartDate)
            .Take(20)
            .ToList();

        return new HealthSummary
        {
            Profile = profile,
            PeriodStart = DateOnly.FromDateTime(cutoff),
            PeriodEnd = DateOnly.FromDateTime(DateTime.Today),
            AnalysisDays = days,
            Metrics = metrics,
            RecentWorkouts = recentWorkouts
        };
    }

    private static MetricSummary? ComputeMetric(string metricType, List<HealthRecord> records)
    {
        var subset = records
            .Where(r => r.Type == metricType)
            .OrderBy(r => r.StartDate)
            .ToList();

        if (subset.Count == 0) return null;

        var dailyPoints = BuildDailyPoints(metricType, subset);
        if (dailyPoints.Count == 0) return null;

        var values = dailyPoints.Select(p => p.Value).ToList();
        var trend = ComputeTrend(values);

        return new MetricSummary
        {
            MetricType = metricType,
            Unit = ResolveUnit(metricType, subset),
            Average = Math.Round(values.Average(), 2),
            Min = values.Min(),
            Max = values.Max(),
            Latest = values[^1],
            SampleCount = dailyPoints.Count,
            Trend = Math.Round(trend, 4)
        };
    }

    /// <summary>
    /// Calcula la pendiente de una regresión lineal simple (tendencia).
    /// Positivo = valor subiendo con el tiempo.
    /// </summary>
    private static double ComputeTrend(List<double> values)
    {
        int n = values.Count;
        if (n < 2) return 0;

        double sumX = 0, sumY = 0, sumXY = 0, sumX2 = 0;
        for (int i = 0; i < n; i++)
        {
            sumX += i;
            sumY += values[i];
            sumXY += i * values[i];
            sumX2 += i * i;
        }

        double denom = n * sumX2 - sumX * sumX;
        return denom == 0 ? 0 : (n * sumXY - sumX * sumY) / denom;
    }

    // Nombres legibles para mostrar en la UI
    private static readonly Dictionary<string, string> MetricDisplayNames = new()
    {
        ["HKQuantityTypeIdentifierStepCount"] = "Pasos/día",
        ["HKQuantityTypeIdentifierHeartRate"] = "Frecuencia cardíaca",
        ["HKQuantityTypeIdentifierRestingHeartRate"] = "FC en reposo",
        ["HKQuantityTypeIdentifierHeartRateVariabilitySDNN"] = "HRV",
        ["HKQuantityTypeIdentifierVO2Max"] = "VO₂ máx",
        ["HKQuantityTypeIdentifierActiveEnergyBurned"] = "Energía activa (kcal)",
        ["HKQuantityTypeIdentifierDistanceWalkingRunning"] = "Distancia (km)",
        ["HKQuantityTypeIdentifierBodyMass"] = "Peso (kg)",
        ["HKQuantityTypeIdentifierOxygenSaturation"] = "SpO₂ (%)",
    };

    public IReadOnlyList<MetricTimeSeries> GetTimeSeries(
        IEnumerable<HealthRecord> records,
        IEnumerable<string> metricTypes,
        int days = 90)
    {
        var cutoff = DateTime.Now.AddDays(-days).Date;
        var result = new List<MetricTimeSeries>();
        var selectedMetricTypes = metricTypes.ToHashSet();

        var grouped = records
            .Where(r => r.StartDate >= cutoff)
            .GroupBy(r => r.Type);

        foreach (var typeGroup in grouped)
        {
            if (!selectedMetricTypes.Contains(typeGroup.Key)) continue;

            var dailyPoints = BuildDailyPoints(typeGroup.Key, typeGroup.OrderBy(r => r.StartDate));

            if (dailyPoints.Count == 0) continue;

            MetricDisplayNames.TryGetValue(typeGroup.Key, out var displayName);
            var unit = ResolveUnit(typeGroup.Key, typeGroup);

            result.Add(new MetricTimeSeries
            {
                MetricType = typeGroup.Key,
                DisplayName = displayName ?? typeGroup.Key.Replace("HKQuantityTypeIdentifier", ""),
                Unit = unit,
                Points = dailyPoints
            });
        }

        return result;
    }

    private static List<DailyDataPoint> BuildDailyPoints(string metricType, IEnumerable<HealthRecord> records) =>
        metricType switch
        {
            SleepMetricType => BuildSleepDailyPoints(records),
            _ when CumulativeMetrics.Contains(metricType) => records
                .GroupBy(r => DateOnly.FromDateTime(r.StartDate))
                .OrderBy(g => g.Key)
                .Select(g => new DailyDataPoint
                {
                    Date = g.Key,
                    Value = Math.Round(g.Sum(r => r.Value), 2)
                })
                .ToList(),
            _ => records
                .GroupBy(r => DateOnly.FromDateTime(r.StartDate))
                .OrderBy(g => g.Key)
                .Select(g => new DailyDataPoint
                {
                    Date = g.Key,
                    Value = Math.Round(g.Average(r => r.Value), 2)
                })
                .ToList()
        };

    private static List<DailyDataPoint> BuildSleepDailyPoints(IEnumerable<HealthRecord> records)
    {
        var totalsByDay = new Dictionary<DateOnly, double>();

        foreach (var record in records.Where(IsAsleepRecord))
        {
            if (record.EndDate <= record.StartDate)
                continue;

            foreach (var (date, hours) in SplitDurationByDay(record.StartDate, record.EndDate))
            {
                totalsByDay[date] = totalsByDay.TryGetValue(date, out var total)
                    ? total + hours
                    : hours;
            }
        }

        return totalsByDay
            .OrderBy(kvp => kvp.Key)
            .Select(kvp => new DailyDataPoint
            {
                Date = kvp.Key,
                Value = Math.Round(kvp.Value, 2)
            })
            .ToList();
    }

    private static IEnumerable<(DateOnly Date, double Hours)> SplitDurationByDay(DateTime start, DateTime end)
    {
        var current = start;

        while (current < end)
        {
            var nextBoundary = current.Date.AddDays(1);
            var segmentEnd = nextBoundary < end ? nextBoundary : end;
            yield return (DateOnly.FromDateTime(current), (segmentEnd - current).TotalHours);
            current = segmentEnd;
        }
    }

    private static bool IsAsleepRecord(HealthRecord record)
    {
        if (!string.Equals(record.Type, SleepMetricType, StringComparison.Ordinal))
            return false;

        return AsleepValues.Contains(record.RawValue);
    }

    private static string ResolveUnit(string metricType, IEnumerable<HealthRecord> records) =>
        metricType == SleepMetricType
            ? "h"
            : records.LastOrDefault()?.Unit ?? string.Empty;
}
