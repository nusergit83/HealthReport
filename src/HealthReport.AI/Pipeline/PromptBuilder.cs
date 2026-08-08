using System.Text.Json;
using HealthReport.Core.Models;

namespace HealthReport.AI.Pipeline;

/// <summary>
/// Construye los prompts para cada fase del análisis.
/// Los prompts incluyen solo los datos relevantes para la fase,
/// manteniendo el JSON por debajo de ~20 KB para ser compatible con modelos pequeños.
/// </summary>
public sealed class PromptBuilder
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    public string BuildPhasePrompt(AnalysisPhase phase, HealthSummary summary) => phase switch
    {
        AnalysisPhase.Demographics => BuildDemographicsPrompt(summary),
        AnalysisPhase.PhysicalActivity => BuildActivityPrompt(summary),
        AnalysisPhase.CardiovascularAndSleep => BuildCardioSleepPrompt(summary),
        _ => throw new ArgumentOutOfRangeException(nameof(phase))
    };

    public string BuildSynthesisPrompt(IReadOnlyList<AnalysisResult> previousPhases)
    {
        var parts = previousPhases
            .Where(p => p.IsSuccess)
            .Select(p => $"### {p.PhaseName}\n{p.Content}");

        var context = string.Join("\n\n", parts);

        return $"""
                Eres un médico especialista en medicina preventiva y salud digital.
                A continuación tienes los análisis parciales de los datos de salud de un usuario.
                Tu tarea es integrar toda la información y generar un informe de salud completo en español.

                El informe debe incluir:
                1. Resumen ejecutivo (3-4 oraciones)
                2. Hallazgos principales por área
                3. Patrones de riesgo detectados (si los hay)
                4. Recomendaciones prioritarias (máximo 5, ordenadas por impacto)
                5. Indicadores positivos a mantener

                Sé claro, empático y basa todo en los datos. No inventes datos.

                --- ANÁLISIS PARCIALES ---
                {context}
                --- FIN DE ANÁLISIS PARCIALES ---

                Genera el informe final ahora:
                """;
    }

    private static string BuildDemographicsPrompt(HealthSummary s)
    {
        var profile = new
        {
            edad = s.Profile.AgeYears,
            sexo = s.Profile.BiologicalSex,
            altura_m = s.Profile.HeightMeters,
            peso_kg = s.Profile.WeightKg,
            imc = s.Profile.Bmi,
            periodo_analisis_dias = s.AnalysisDays,
            fecha_inicio = s.PeriodStart.ToString("yyyy-MM-dd"),
            fecha_fin = s.PeriodEnd.ToString("yyyy-MM-dd")
        };

        var json = JsonSerializer.Serialize(profile, JsonOpts);

        return $"""
                Eres un médico especialista en medicina preventiva.
                Analiza el siguiente perfil demográfico de un usuario de Apple Health y proporciona:
                1. Descripción del perfil del usuario con sus valores de referencia según edad y sexo.
                2. Interpretación del IMC y estado ponderal.
                3. Observaciones relevantes sobre el perfil.

                Responde en español, de forma clara y profesional. Máximo 300 palabras.

                DATOS DEL PERFIL:
                {json}
                """;
    }

    private static string BuildActivityPrompt(HealthSummary s)
    {
        var activityMetrics = new[]
        {
            "HKQuantityTypeIdentifierStepCount",
            "HKQuantityTypeIdentifierActiveEnergyBurned",
            "HKQuantityTypeIdentifierBasalEnergyBurned",
            "HKQuantityTypeIdentifierDistanceWalkingRunning"
        };

        var data = FilterMetrics(s.Metrics, activityMetrics);
        var workouts = s.RecentWorkouts.Take(10).Select(w => new
        {
            tipo = w.ActivityType.Replace("HKWorkoutActivityType", ""),
            duracion_min = Math.Round(w.DurationMinutes, 1),
            energia_kcal = Math.Round(w.TotalEnergyKcal, 0),
            distancia_km = Math.Round(w.TotalDistanceKm, 2),
            fecha = w.StartDate.ToString("yyyy-MM-dd")
        });

        var json = JsonSerializer.Serialize(new { metricas = data, entrenamientos_recientes = workouts }, JsonOpts);

        return $"""
                Eres un médico especialista en medicina del deporte y salud preventiva.
                Analiza la siguiente información de actividad física de los últimos {s.AnalysisDays} días y proporciona:
                1. Evaluación del nivel de actividad física (sedentario / moderado / activo / muy activo).
                2. Comparación con las recomendaciones de la OMS (150 min/semana actividad moderada).
                3. Tendencias observadas (mejora, empeoramiento, estabilidad).
                4. Recomendaciones específicas de mejora.

                Responde en español, de forma clara y profesional. Máximo 350 palabras.

                DATOS DE ACTIVIDAD:
                {json}
                """;
    }

    private static string BuildCardioSleepPrompt(HealthSummary s)
    {
        var cardioMetrics = new[]
        {
            "HKQuantityTypeIdentifierHeartRate",
            "HKQuantityTypeIdentifierRestingHeartRate",
            "HKQuantityTypeIdentifierHeartRateVariabilitySDNN",
            "HKQuantityTypeIdentifierVO2Max",
            "HKQuantityTypeIdentifierOxygenSaturation",
            "HKQuantityTypeIdentifierRespiratoryRate",
            "HKCategoryTypeIdentifierSleepAnalysis",
            "HKQuantityTypeIdentifierAppleSleepingBreathingDisturbances"
        };

        var data = FilterMetrics(s.Metrics, cardioMetrics);
        var json = JsonSerializer.Serialize(new { metricas = data }, JsonOpts);

        return $"""
                Eres un médico cardiólogo y especialista en medicina del sueño.
                Analiza la siguiente información cardiovascular y de sueño de los últimos {s.AnalysisDays} días y proporciona:
                1. Evaluación de la frecuencia cardíaca en reposo y su variabilidad (HRV).
                2. Interpretación del VO₂ máx y capacidad aeróbica.
                3. Evaluación de la calidad del sueño y patrones detectados.
                4. Señales de alerta a tener en cuenta (si las hay).
                5. Recomendaciones cardiovasculares y de higiene del sueño.

                Responde en español, de forma clara y profesional. Máximo 400 palabras.

                DATOS CARDIOVASCULARES Y DE SUEÑO:
                {json}
                """;
    }

    private static object FilterMetrics(IEnumerable<MetricSummary> metrics, string[] types)
    {
        return metrics
            .Where(m => types.Contains(m.MetricType))
            .Select(m => new
            {
                metrica = m.MetricType.Replace("HKQuantityTypeIdentifier", "").Replace("HKCategoryTypeIdentifier", ""),
                unidad = m.Unit,
                media = m.Average,
                minimo = m.Min,
                maximo = m.Max,
                ultimo_valor = m.Latest,
                muestras = m.SampleCount,
                tendencia = m.Trend > 0.01 ? "subiendo" : m.Trend < -0.01 ? "bajando" : "estable"
            })
            .ToList();
    }
}
