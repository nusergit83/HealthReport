using System.Text;
using FluentAssertions;
using HealthReport.Core.Parsing;

namespace HealthReport.Tests.Parsing;

public sealed class AppleHealthXmlParserTests
{
    private readonly AppleHealthXmlParser _parser = new();

    private static Stream ToStream(string xml) =>
        new MemoryStream(Encoding.UTF8.GetBytes(xml));

    // ── Perfil de usuario ───────────────────────────────────────────────

    [Fact]
    public async Task ParseAsync_WithValidProfile_ReturnsCorrectProfile()
    {
        var xml = """
                  <?xml version="1.0" encoding="UTF-8"?>
                  <HealthData locale="es_ES">
                    <Me DateOfBirth="1990-06-15"
                        HKCharacteristicTypeIdentifierBiologicalSex="HKBiologicalSexMale"
                        HeightInMeters="1.80"
                        WeightInKilograms="80.5"/>
                  </HealthData>
                  """;

        var (profile, records, workouts) = await _parser.ParseAsync(ToStream(xml));

        profile.DateOfBirth.Should().Be(new DateOnly(1990, 6, 15));
        profile.BiologicalSex.Should().Be("Masculino");
        profile.HeightMeters.Should().BeApproximately(1.80, 0.001);
        profile.WeightKg.Should().BeApproximately(80.5, 0.001);
        profile.Bmi.Should().BeApproximately(24.8, 0.2);
        records.Should().BeEmpty();
        workouts.Should().BeEmpty();
    }

    [Fact]
    public async Task ParseAsync_WithFemaleSex_TranslatesCorrectly()
    {
        var xml = """
                  <?xml version="1.0" encoding="UTF-8"?>
                  <HealthData locale="es_ES">
                    <Me HKCharacteristicTypeIdentifierBiologicalSex="HKBiologicalSexFemale"/>
                  </HealthData>
                  """;

        var (profile, _, _) = await _parser.ParseAsync(ToStream(xml));

        profile.BiologicalSex.Should().Be("Femenino");
    }

    [Fact]
    public async Task ParseAsync_WithMissingProfile_ReturnsDefaultProfile()
    {
        var xml = """
                  <?xml version="1.0" encoding="UTF-8"?>
                  <HealthData locale="es_ES">
                  </HealthData>
                  """;

        var (profile, _, _) = await _parser.ParseAsync(ToStream(xml));

        profile.HeightMeters.Should().BeNull();
        profile.WeightKg.Should().BeNull();
        profile.Bmi.Should().BeNull();
    }

    // ── Records ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ParseAsync_WithValidRecord_ReturnsRecord()
    {
        var xml = """
                  <?xml version="1.0" encoding="UTF-8"?>
                  <HealthData locale="es_ES">
                    <Me/>
                    <Record type="HKQuantityTypeIdentifierStepCount"
                            sourceName="iPhone"
                            unit="count"
                            value="8542"
                            startDate="2026-07-01 09:00:00 +0200"
                            endDate="2026-07-01 09:00:00 +0200"/>
                  </HealthData>
                  """;

        var (_, records, _) = await _parser.ParseAsync(ToStream(xml));

        records.Should().HaveCount(1);
        var r = records[0];
        r.Type.Should().Be("HKQuantityTypeIdentifierStepCount");
        r.Value.Should().Be(8542);
        r.Unit.Should().Be("count");
        r.SourceName.Should().Be("iPhone");
        r.StartDate.Should().Be(new DateTime(2026, 7, 1, 9, 0, 0));
    }

    [Fact]
    public async Task ParseAsync_RecordWithNonNumericValue_IsDiscarded()
    {
        var xml = """
                  <?xml version="1.0" encoding="UTF-8"?>
                  <HealthData locale="es_ES">
                    <Me/>
                    <Record type="HKCategoryTypeIdentifierSleepAnalysis"
                            value="HKCategoryValueSleepAnalysisAsleep"
                            startDate="2026-07-01 23:00:00 +0200"
                            endDate="2026-07-02 07:00:00 +0200"/>
                  </HealthData>
                  """;

        var (_, records, _) = await _parser.ParseAsync(ToStream(xml));

        // Los valores no numéricos se descartan silenciosamente
        records.Should().BeEmpty();
    }

    [Fact]
    public async Task ParseAsync_RecordWithNumericSleepValue_IsParsed()
    {
        var xml = """
                  <?xml version="1.0" encoding="UTF-8"?>
                  <HealthData locale="es_ES">
                    <Me/>
                    <Record type="HKCategoryTypeIdentifierSleepAnalysis"
                            value="1"
                            startDate="2026-07-01 23:00:00 +0200"
                            endDate="2026-07-02 07:00:00 +0200"/>
                  </HealthData>
                  """;

        var (_, records, _) = await _parser.ParseAsync(ToStream(xml));

        records.Should().HaveCount(1);
        records[0].Value.Should().Be(1);
    }

    [Fact]
    public async Task ParseAsync_MultipleRecords_AllParsed()
    {
        var xml = """
                  <?xml version="1.0" encoding="UTF-8"?>
                  <HealthData locale="es_ES">
                    <Me/>
                    <Record type="HKQuantityTypeIdentifierHeartRate" value="72"
                            startDate="2026-07-01 08:00:00 +0200" endDate="2026-07-01 08:00:00 +0200" unit="count/min"/>
                    <Record type="HKQuantityTypeIdentifierStepCount" value="500"
                            startDate="2026-07-01 09:00:00 +0200" endDate="2026-07-01 09:00:00 +0200" unit="count"/>
                    <Record type="HKQuantityTypeIdentifierVO2Max" value="42.5"
                            startDate="2026-07-01 10:00:00 +0200" endDate="2026-07-01 10:00:00 +0200" unit="ml/min/kg"/>
                  </HealthData>
                  """;

        var (_, records, _) = await _parser.ParseAsync(ToStream(xml));

        records.Should().HaveCount(3);
        records.Select(r => r.Type).Should().Contain([
            "HKQuantityTypeIdentifierHeartRate",
            "HKQuantityTypeIdentifierStepCount",
            "HKQuantityTypeIdentifierVO2Max"
        ]);
    }

    [Fact]
    public async Task ParseAsync_RecordMissingDate_IsDiscarded()
    {
        var xml = """
                  <?xml version="1.0" encoding="UTF-8"?>
                  <HealthData locale="es_ES">
                    <Me/>
                    <Record type="HKQuantityTypeIdentifierStepCount" value="100"/>
                  </HealthData>
                  """;

        var (_, records, _) = await _parser.ParseAsync(ToStream(xml));

        records.Should().BeEmpty();
    }

    // ── Workouts ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ParseAsync_WithValidWorkout_ReturnsWorkout()
    {
        var xml = """
                  <?xml version="1.0" encoding="UTF-8"?>
                  <HealthData locale="es_ES">
                    <Me/>
                    <Workout workoutActivityType="HKWorkoutActivityTypeRunning"
                             duration="45.5"
                             totalEnergyBurned="380"
                             totalDistance="7.2"
                             startDate="2026-07-15 07:30:00 +0200"
                             endDate="2026-07-15 08:15:30 +0200"/>
                  </HealthData>
                  """;

        var (_, _, workouts) = await _parser.ParseAsync(ToStream(xml));

        workouts.Should().HaveCount(1);
        var w = workouts[0];
        w.ActivityType.Should().Be("HKWorkoutActivityTypeRunning");
        w.DurationMinutes.Should().BeApproximately(45.5, 0.01);
        w.TotalEnergyKcal.Should().BeApproximately(380, 0.01);
        w.TotalDistanceKm.Should().BeApproximately(7.2, 0.01);
    }

    // ── Fechas en distintos formatos ──────────────────────────────────────

    [Theory]
    [InlineData("2026-07-01 09:00:00 +0200")]
    [InlineData("2026-07-01T09:00:00+02:00")]
    [InlineData("2026-07-01")]
    public async Task ParseAsync_DateFormats_AllSupported(string dateStr)
    {
        var xml = $"""
                   <?xml version="1.0" encoding="UTF-8"?>
                   <HealthData locale="es_ES">
                     <Me/>
                     <Record type="HKQuantityTypeIdentifierStepCount" value="100"
                             startDate="{dateStr}" endDate="{dateStr}" unit="count"/>
                   </HealthData>
                   """;

        var (_, records, _) = await _parser.ParseAsync(ToStream(xml));

        records.Should().HaveCount(1);
        records[0].StartDate.Should().NotBe(DateTime.MinValue);
    }

    // ── Cancellación ─────────────────────────────────────────────────────

    [Fact]
    public async Task ParseAsync_WhenCancelled_ThrowsOperationCancelled()
    {
        var xml = """
                  <?xml version="1.0" encoding="UTF-8"?>
                  <HealthData locale="es_ES">
                    <Me/>
                  </HealthData>
                  """;

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _parser.ParseAsync(ToStream(xml), cancellationToken: cts.Token));
    }
}
