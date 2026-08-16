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

    [Fact]
    public void Aggregate_CumulativeMetrics_UseDailyTotals()
    {
        var day1 = DateTime.Today.AddDays(-2).AddHours(8);
        var day2 = DateTime.Today.AddDays(-1).AddHours(8);
        var records = new List<HealthRecord>
        {
            MakeRecord("HKQuantityTypeIdentifierStepCount", 2000, day1),
            MakeRecord("HKQuantityTypeIdentifierStepCount", 3000, day1.AddHours(2)),
            MakeRecord("HKQuantityTypeIdentifierStepCount", 7000, day2)
        };

        var summary = _aggregator.Aggregate(DefaultProfile, records, []);
        var steps = GetMetric(summary, "StepCount");

        steps.Should().NotBeNull();
        steps!.Average.Should().Be(6000);
        steps.Min.Should().Be(5000);
        steps.Max.Should().Be(7000);
        steps.Latest.Should().Be(7000);
        steps.SampleCount.Should().Be(2);
    }

    [Fact]
    public void Aggregate_SleepMetric_UsesDailyAsleepHours()
    {
        var sleepStart = DateTime.Today.AddDays(-2).AddHours(23);
        var sleepEnd = sleepStart.AddHours(8);
        var records = new List<HealthRecord>
        {
            MakeSleepRecord("HKCategoryValueSleepAnalysisAsleepCore", sleepStart, sleepEnd),
            MakeSleepRecord("HKCategoryValueSleepAnalysisInBed", DateTime.Today.AddDays(-1).AddHours(22), DateTime.Today.AddDays(-1).AddHours(22.5))
        };

        var summary = _aggregator.Aggregate(DefaultProfile, records, []);
        var sleep = GetMetric(summary, "SleepAnalysis");

        sleep.Should().NotBeNull();
        sleep!.Unit.Should().Be("h");
        sleep.Average.Should().Be(4);
        sleep.Min.Should().Be(1);
        sleep.Max.Should().Be(7);
        sleep.SampleCount.Should().Be(2);
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

    [Fact]
    public void GetTimeSeries_CumulativeMetric_UsesDailySums()
    {
        var day = DateTime.Today.AddDays(-2).AddHours(8);
        var records = new List<HealthRecord>
        {
            MakeRecord("HKQuantityTypeIdentifierStepCount", 1200, day),
            MakeRecord("HKQuantityTypeIdentifierStepCount", 800, day.AddHours(1)),
            MakeRecord("HKQuantityTypeIdentifierStepCount", 3500, day.AddDays(1))
        };

        var series = _aggregator.GetTimeSeries(records, ["HKQuantityTypeIdentifierStepCount"], days: 10);

        series.Should().HaveCount(1);
        series[0].Points.Select(p => p.Value).Should().Equal([2000, 3500]);
    }

    // ── Helpers ───────────────────────────────────────────────────────

    private static HealthRecord MakeRecord(string type, double value, DateTime date) =>
        new() { Type = type, RawValue = value.ToString(System.Globalization.CultureInfo.InvariantCulture), Value = value, StartDate = date, EndDate = date, Unit = "count" };

    private static HealthRecord MakeSleepRecord(string rawValue, DateTime startDate, DateTime endDate) =>
        new()
        {
            Type = "HKCategoryTypeIdentifierSleepAnalysis",
            RawValue = rawValue,
            StartDate = startDate,
            EndDate = endDate,
            Unit = string.Empty
        };

    private static List<HealthRecord> MakeRecords(string type, double[] values, DateTime baseDate) =>
        values.Select((v, i) => MakeRecord(type, v, baseDate.AddDays(i))).ToList();

    private static DateTime DaysAgo(int days) => DateTime.Today.AddDays(-days);

    private static MetricSummary? GetMetric(HealthSummary summary, string partialName) =>
        summary.Metrics.FirstOrDefault(m => m.MetricType.Contains(partialName));
}
