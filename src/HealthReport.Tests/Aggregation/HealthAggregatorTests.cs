using FluentAssertions;
using HealthReport.Core.Aggregation;
using HealthReport.Core.Models;

namespace HealthReport.Tests.Aggregation;

public sealed class HealthAggregatorTests
{
    private readonly HealthAggregator _aggregator = new();
    private static readonly UserProfile DefaultProfile = new();

    // ── Filtrado temporal ─────────────────────────────────────────────

    [Fact]
    public void Aggregate_ExcludesRecordsOlderThanPeriod()
    {
        var records = new List<HealthRecord>
        {
            MakeRecord("HKQuantityTypeIdentifierStepCount", 1000, DateTime.Today.AddDays(-200)),
            MakeRecord("HKQuantityTypeIdentifierStepCount", 5000, DateTime.Today.AddDays(-10)),
        };

        var summary = _aggregator.Aggregate(DefaultProfile, records, [], days: 90);

        var steps = summary.Metrics.FirstOrDefault(m => m.MetricType.Contains("StepCount"));
        steps.Should().NotBeNull();
        steps!.SampleCount.Should().Be(1);
        steps.Average.Should().Be(5000);
    }

    // ── Estadísticas básicas ──────────────────────────────────────────

    [Fact]
    public void Aggregate_ComputesCorrectAvgMinMax()
    {
        var records = MakeRecords("HKQuantityTypeIdentifierHeartRate",
            [60, 70, 80, 90], DaysAgo(30));

        var summary = _aggregator.Aggregate(DefaultProfile, records, []);
        var hr = GetMetric(summary, "HeartRate");

        hr.Should().NotBeNull();
        hr!.Average.Should().Be(75);
        hr.Min.Should().Be(60);
        hr.Max.Should().Be(90);
        hr.Latest.Should().Be(90);
        hr.SampleCount.Should().Be(4);
    }

    // ── Tendencia ─────────────────────────────────────────────────────

    [Fact]
    public void Aggregate_AscendingValues_HasPositiveTrend()
    {
        var records = MakeRecords("HKQuantityTypeIdentifierBodyMass",
            [70, 71, 72, 73, 74], DaysAgo(20));

        var summary = _aggregator.Aggregate(DefaultProfile, records, []);
        var metric = GetMetric(summary, "BodyMass");

        metric.Should().NotBeNull();
        metric!.Trend.Should().BePositive();
    }

    [Fact]
    public void Aggregate_DescendingValues_HasNegativeTrend()
    {
        var records = MakeRecords("HKQuantityTypeIdentifierBodyMass",
            [80, 79, 78, 77, 76], DaysAgo(20));

        var summary = _aggregator.Aggregate(DefaultProfile, records, []);
        var metric = GetMetric(summary, "BodyMass");

        metric!.Trend.Should().BeNegative();
    }

    [Fact]
    public void Aggregate_ConstantValues_HasZeroTrend()
    {
        var records = MakeRecords("HKQuantityTypeIdentifierBodyMass",
            [75, 75, 75, 75], DaysAgo(20));

        var summary = _aggregator.Aggregate(DefaultProfile, records, []);
        var metric = GetMetric(summary, "BodyMass");

        metric!.Trend.Should().BeApproximately(0, 0.0001);
    }

    // ── Métricas no relevantes ────────────────────────────────────────

    [Fact]
    public void Aggregate_UnknownMetric_IsExcluded()
    {
        var records = new List<HealthRecord>
        {
            MakeRecord("HKQuantityTypeIdentifierSomeObscureMetric", 42, DateTime.Today.AddDays(-5))
        };

        var summary = _aggregator.Aggregate(DefaultProfile, records, []);

        summary.Metrics.Should().NotContain(m => m.MetricType.Contains("Obscure"));
    }

    // ── Sin datos ─────────────────────────────────────────────────────

    [Fact]
    public void Aggregate_EmptyInput_ReturnsEmptySummary()
    {
        var summary = _aggregator.Aggregate(DefaultProfile, [], []);

        summary.Metrics.Should().BeEmpty();
        summary.RecentWorkouts.Should().BeEmpty();
    }

    // ── Perfil y periodo ──────────────────────────────────────────────

    [Fact]
    public void Aggregate_SetsCorrectPeriod()
    {
        var summary = _aggregator.Aggregate(DefaultProfile, [], [], days: 60);

        summary.AnalysisDays.Should().Be(60);
        summary.PeriodEnd.Should().Be(DateOnly.FromDateTime(DateTime.Today));
        summary.PeriodStart.Should().Be(DateOnly.FromDateTime(DateTime.Today.AddDays(-60)));
    }

    [Fact]
    public void Aggregate_LimitsRecentWorkoutsTo20()
    {
        var workouts = Enumerable.Range(1, 30)
            .Select(i => new WorkoutRecord
            {
                ActivityType = "HKWorkoutActivityTypeRunning",
                StartDate = DateTime.Today.AddDays(-i),
                EndDate = DateTime.Today.AddDays(-i).AddHours(1)
            }).ToList();

        var summary = _aggregator.Aggregate(DefaultProfile, [], workouts);

        summary.RecentWorkouts.Should().HaveCount(20);
    }

    // ── Helpers ───────────────────────────────────────────────────────

    private static HealthRecord MakeRecord(string type, double value, DateTime date) =>
        new() { Type = type, Value = value, StartDate = date, EndDate = date, Unit = "count" };

    private static List<HealthRecord> MakeRecords(string type, double[] values, DateTime baseDate) =>
        values.Select((v, i) => MakeRecord(type, v, baseDate.AddDays(i))).ToList();

    private static DateTime DaysAgo(int days) => DateTime.Today.AddDays(-days);

    private static MetricSummary? GetMetric(HealthSummary summary, string partialName) =>
        summary.Metrics.FirstOrDefault(m => m.MetricType.Contains(partialName));
}
