using HealthReport.Core.Models;

namespace HealthReport.AI.Pipeline;

public interface IAnalysisPipeline
{
    /// <summary>
    /// Ejecuta el pipeline completo de análisis fase a fase.
    /// Llama a <paramref name="onPhaseComplete"/> al terminar cada fase
    /// y a <paramref name="onToken"/> con cada token de texto generado.
    /// </summary>
    Task<IReadOnlyList<AnalysisResult>> RunAsync(
        HealthSummary summary,
        string model,
        Func<AnalysisPhase, string, Task> onToken,
        Func<AnalysisResult, Task> onPhaseComplete,
        CancellationToken cancellationToken = default);
}
