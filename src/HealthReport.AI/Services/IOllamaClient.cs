namespace HealthReport.AI.Services;

public interface IOllamaClient
{
    /// <summary>
    /// Obtiene la lista de modelos disponibles en el servidor Ollama.
    /// </summary>
    Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Envía un prompt al modelo y devuelve la respuesta completa.
    /// </summary>
    Task<string> GenerateAsync(string model, string prompt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Envía un prompt y llama a <paramref name="onToken"/> por cada token recibido (streaming).
    /// </summary>
    Task StreamAsync(string model, string prompt, Func<string, Task> onToken, CancellationToken cancellationToken = default);
}
