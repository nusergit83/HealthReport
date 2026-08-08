using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HealthReport.AI.Services;

/// <summary>
/// Cliente HTTP para la API REST de Ollama (http://localhost:11434).
/// Documentación: https://github.com/ollama/ollama/blob/main/docs/api.md
/// </summary>
public sealed class OllamaClient : IOllamaClient, IDisposable
{
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public OllamaClient(string baseUrl = "http://localhost:11434")
    {
        _http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromMinutes(10) };
    }

    public async Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken cancellationToken = default)
    {
        var response = await _http.GetFromJsonAsync<TagsResponse>("/api/tags", cancellationToken).ConfigureAwait(false);
        return response?.Models?.Select(m => m.Name).ToList() ?? [];
    }

    public async Task<string> GenerateAsync(string model, string prompt, CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder();
        await StreamAsync(model, prompt, token => { sb.Append(token); return Task.CompletedTask; }, cancellationToken).ConfigureAwait(false);
        return sb.ToString();
    }

    public async Task StreamAsync(string model, string prompt, Func<string, Task> onToken, CancellationToken cancellationToken = default)
    {
        var body = JsonSerializer.Serialize(new { model, prompt, stream = true }, JsonOptions);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/generate")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new System.IO.StreamReader(stream);

        while (!reader.EndOfStream)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(line)) continue;

            var chunk = JsonSerializer.Deserialize<GenerateChunk>(line, JsonOptions);
            if (chunk?.Response is not null)
                await onToken(chunk.Response).ConfigureAwait(false);

            if (chunk?.Done == true) break;
        }
    }

    public void Dispose() => _http.Dispose();

    // --- DTOs internos ---
    private sealed record TagsResponse([property: JsonPropertyName("models")] List<ModelInfo>? Models);
    private sealed record ModelInfo([property: JsonPropertyName("name")] string Name);
    private sealed record GenerateChunk(
        [property: JsonPropertyName("response")] string? Response,
        [property: JsonPropertyName("done")] bool Done);
}
