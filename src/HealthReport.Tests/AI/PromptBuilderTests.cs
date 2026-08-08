using FluentAssertions;
using HealthReport.AI.Pipeline;
using HealthReport.Core.Models;

namespace HealthReport.Tests.AI;

public sealed class PromptBuilderTests
{
    private readonly PromptBuilder _builder = new();

    private static HealthSummary MakeSummary(List<MetricSummary>? metrics = null) => new()
    {
        Profile = new UserProfile
        {
            DateOfBirth = new DateOnly(1985, 3, 10),
            BiologicalSex = "Masculino",
            HeightMeters = 1.75,
            WeightKg = 74.0
        },
        PeriodStart = DateOnly.FromDateTime(DateTime.Today.AddDays(-90)),
        PeriodEnd = DateOnly.FromDateTime(DateTime.Today),
        AnalysisDays = 90,
        Metrics = metrics ?? []
    };

    private static MetricSummary MakeMetric(string type, double avg = 70, string unit = "count") =>
        new() { MetricType = type, Average = avg, Min = avg - 5, Max = avg + 5, Latest = avg, SampleCount = 100, Unit = unit };

    // ── Tamaño de prompts ─────────────────────────────────────────────

    [Theory]
    [InlineData(AnalysisPhase.Demographics)]
    [InlineData(AnalysisPhase.PhysicalActivity)]
    [InlineData(AnalysisPhase.CardiovascularAndSleep)]
    public void BuildPhasePrompt_SizeIsBelow30KB(AnalysisPhase phase)
    {
        var metrics = new List<MetricSummary>
        {
            MakeMetric("HKQuantityTypeIdentifierStepCount", 8000, "count"),
            MakeMetric("HKQuantityTypeIdentifierHeartRate", 72, "count/min"),
            MakeMetric("HKQuantityTypeIdentifierRestingHeartRate", 58, "count/min"),
            MakeMetric("HKQuantityTypeIdentifierVO2Max", 42, "ml/min/kg"),
            MakeMetric("HKQuantityTypeIdentifierActiveEnergyBurned", 450, "kcal"),
            MakeMetric("HKQuantityTypeIdentifierDistanceWalkingRunning", 6.2, "km"),
            MakeMetric("HKQuantityTypeIdentifierOxygenSaturation", 98, "%"),
            MakeMetric("HKCategoryTypeIdentifierSleepAnalysis", 2, ""),
        };

        var prompt = _builder.BuildPhasePrompt(phase, MakeSummary(metrics));

        var sizeKb = System.Text.Encoding.UTF8.GetByteCount(prompt) / 1024.0;
        sizeKb.Should().BeLessThan(30, $"el prompt de {phase} debería caber en 30 KB");
    }

    // ── Contenido de prompts ──────────────────────────────────────────

    [Fact]
    public void BuildPhasePrompt_Demographics_ContainsAgeAndBmi()
    {
        var summary = MakeSummary();
        var prompt = _builder.BuildPhasePrompt(AnalysisPhase.Demographics, summary);

        prompt.Should().Contain("imc");
        prompt.Should().Contain("edad");
        prompt.Should().Contain("español");
    }

    [Fact]
    public void BuildPhasePrompt_Activity_ContainsStepData()
    {
        var metrics = new List<MetricSummary>
        {
            MakeMetric("HKQuantityTypeIdentifierStepCount", 9000, "count")
        };
        var prompt = _builder.BuildPhasePrompt(AnalysisPhase.PhysicalActivity, MakeSummary(metrics));

        prompt.Should().Contain("StepCount");
        prompt.Should().Contain("OMS");
    }

    [Fact]
    public void BuildPhasePrompt_CardioSleep_ContainsHrvAndVo2()
    {
        var metrics = new List<MetricSummary>
        {
            MakeMetric("HKQuantityTypeIdentifierHeartRateVariabilitySDNN", 45, "ms"),
            MakeMetric("HKQuantityTypeIdentifierVO2Max", 40, "ml/min/kg"),
        };
        var prompt = _builder.BuildPhasePrompt(AnalysisPhase.CardiovascularAndSleep, MakeSummary(metrics));

        prompt.Should().ContainAny("HRV", "variabilidad");
        prompt.Should().ContainAny("VO2Max", "VO₂");
    }

    // ── Síntesis ──────────────────────────────────────────────────────

    [Fact]
    public void BuildSynthesisPrompt_IncludesPreviousPhaseContent()
    {
        var phases = new List<AnalysisResult>
        {
            new() { PhaseName = "Fase 1: Perfil", Content = "Perfil: hombre de 41 años.", IsSuccess = true },
            new() { PhaseName = "Fase 2: Actividad", Content = "Actividad moderada.", IsSuccess = true },
            new() { PhaseName = "Fase 3: Cardio", Content = "FC normal.", IsSuccess = true },
        };

        var prompt = _builder.BuildSynthesisPrompt(phases);

        prompt.Should().Contain("hombre de 41 años");
        prompt.Should().Contain("Actividad moderada");
        prompt.Should().Contain("FC normal");
        prompt.Should().Contain("informe");
    }

    [Fact]
    public void BuildSynthesisPrompt_ExcludesFailedPhases()
    {
        var phases = new List<AnalysisResult>
        {
            new() { PhaseName = "Fase 1", Content = "Datos ok.", IsSuccess = true },
            new() { PhaseName = "Fase 2", Content = "Contenido secreto.", IsSuccess = false, ErrorMessage = "timeout" },
        };

        var prompt = _builder.BuildSynthesisPrompt(phases);

        prompt.Should().NotContain("Contenido secreto");
    }

    [Fact]
    public void BuildSynthesisPrompt_SizeIsBelow30KB()
    {
        var phases = Enumerable.Range(1, 3).Select(i => new AnalysisResult
        {
            PhaseName = $"Fase {i}",
            Content = new string('x', 5000), // 5KB por fase = 15KB total
            IsSuccess = true
        }).ToList();

        var prompt = _builder.BuildSynthesisPrompt(phases);

        var sizeKb = System.Text.Encoding.UTF8.GetByteCount(prompt) / 1024.0;
        sizeKb.Should().BeLessThan(30);
    }
}
