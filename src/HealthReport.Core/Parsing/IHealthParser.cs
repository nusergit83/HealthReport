using HealthReport.Core.Models;

namespace HealthReport.Core.Parsing;

public interface IHealthParser
{
    /// <summary>
    /// Parsea el archivo export.xml en streaming y devuelve los datos estructurados.
    /// </summary>
    /// <param name="xmlStream">Stream del export.xml (puede ser muy grande).</param>
    /// <param name="progress">Progreso opcional (registros procesados).</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    Task<(UserProfile Profile, List<HealthRecord> Records, List<WorkoutRecord> Workouts)> ParseAsync(
        Stream xmlStream,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default);
}
