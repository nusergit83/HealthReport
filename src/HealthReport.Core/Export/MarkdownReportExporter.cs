using HealthReport.Core.Models;

namespace HealthReport.Core.Export;

public sealed class MarkdownReportExporter : IReportExporter
{
    public async Task ExportAsync(
        IEnumerable<AnalysisResult> phases,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        await using var writer = new StreamWriter(outputPath, append: false, System.Text.Encoding.UTF8);

        await writer.WriteLineAsync($"# Informe de Salud — {DateTime.Now:dd/MM/yyyy HH:mm}");
        await writer.WriteLineAsync();

        foreach (var phase in phases)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await writer.WriteLineAsync($"## {phase.PhaseName}");
            await writer.WriteLineAsync();

            if (phase.IsSuccess)
                await writer.WriteLineAsync(phase.Content);
            else
                await writer.WriteLineAsync($"> ⚠️ Error en esta fase: {phase.ErrorMessage}");

            await writer.WriteLineAsync();
            await writer.WriteLineAsync("---");
            await writer.WriteLineAsync();
        }

        await writer.WriteLineAsync($"*Generado el {DateTime.Now:F}*");
    }
}
