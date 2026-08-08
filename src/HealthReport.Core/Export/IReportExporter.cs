using HealthReport.Core.Models;

namespace HealthReport.Core.Export;

public interface IReportExporter
{
    Task ExportAsync(IEnumerable<AnalysisResult> phases, string outputPath, CancellationToken cancellationToken = default);
}
