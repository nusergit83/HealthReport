using System.Text;
using HealthReport.AI.Services;
using HealthReport.Core.Models;

namespace HealthReport.AI.Pipeline;

/// <summary>
/// Orquesta las 4 fases de análisis, enviando resúmenes JSON compactos al modelo de IA local.
/// Cada fase recibe solo los datos que necesita para mantenerse dentro de la ventana de contexto.
/// </summary>
public sealed class AnalysisPipeline : IAnalysisPipeline
{
    private readonly IOllamaClient _ollama;
    private readonly PromptBuilder _promptBuilder;

    public AnalysisPipeline(IOllamaClient ollama)
    {
        _ollama = ollama;
        _promptBuilder = new PromptBuilder();
    }

    public async Task<IReadOnlyList<AnalysisResult>> RunAsync(
        HealthSummary summary,
        string model,
        Func<AnalysisPhase, string, Task> onToken,
        Func<AnalysisResult, Task> onPhaseComplete,
        CancellationToken cancellationToken = default)
    {
        var results = new List<AnalysisResult>();

        // Las 3 primeras fases se ejecutan con datos de salud.
        var dataPhases = new[]
        {
            AnalysisPhase.Demographics,
            AnalysisPhase.PhysicalActivity,
            AnalysisPhase.CardiovascularAndSleep
        };

        foreach (var phase in dataPhases)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await RunPhaseAsync(phase, summary, model, null, onToken, cancellationToken).ConfigureAwait(false);
            results.Add(result);
            await onPhaseComplete(result).ConfigureAwait(false);
        }

        // Fase 4: síntesis final usando los textos ya generados.
        cancellationToken.ThrowIfCancellationRequested();
        var synthesis = await RunPhaseAsync(
            AnalysisPhase.FinalSynthesis, summary, model, results, onToken, cancellationToken).ConfigureAwait(false);
        results.Add(synthesis);
        await onPhaseComplete(synthesis).ConfigureAwait(false);

        return results;
    }

    private async Task<AnalysisResult> RunPhaseAsync(
        AnalysisPhase phase,
        HealthSummary summary,
        string model,
        List<AnalysisResult>? previousResults,
        Func<AnalysisPhase, string, Task> onToken,
        CancellationToken cancellationToken)
    {
        var phaseName = GetPhaseName(phase);
        var sb = new StringBuilder();

        try
        {
            var prompt = phase == AnalysisPhase.FinalSynthesis
                ? _promptBuilder.BuildSynthesisPrompt(previousResults!)
                : _promptBuilder.BuildPhasePrompt(phase, summary);

            await _ollama.StreamAsync(model, prompt,
                async token =>
                {
                    sb.Append(token);
                    await onToken(phase, token).ConfigureAwait(false);
                },
                cancellationToken).ConfigureAwait(false);

            return new AnalysisResult
            {
                PhaseName = phaseName,
                Content = sb.ToString(),
                IsSuccess = true
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new AnalysisResult
            {
                PhaseName = phaseName,
                Content = sb.ToString(),
                IsSuccess = false,
                ErrorMessage = ex.Message
            };
        }
    }

    private static string GetPhaseName(AnalysisPhase phase) => phase switch
    {
        AnalysisPhase.Demographics => "Fase 1: Perfil demográfico",
        AnalysisPhase.PhysicalActivity => "Fase 2: Actividad física",
        AnalysisPhase.CardiovascularAndSleep => "Fase 3: Salud cardiovascular y sueño",
        AnalysisPhase.FinalSynthesis => "Fase 4: Síntesis final",
        _ => phase.ToString()
    };
}
