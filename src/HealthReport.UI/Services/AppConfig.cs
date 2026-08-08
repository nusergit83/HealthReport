using System.IO;
using System.Text.Json;

namespace HealthReport.UI.Services;

/// <summary>
/// Configuración persistente de la aplicación guardada en AppData del usuario.
/// </summary>
public sealed class AppConfig
{
    private static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "HealthReport",
        "config.json");

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public string OllamaUrl { get; set; } = "http://localhost:11434";
    public string LastModel { get; set; } = string.Empty;
    public int AnalysisDays { get; set; } = 90;
    public string LastZipFolder { get; set; } = string.Empty;
    public string LastOutputFolder { get; set; } = string.Empty;

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                return JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
            }
        }
        catch { /* si el archivo está corrupto, usar defaults */ }
        return new AppConfig();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(this, JsonOpts));
        }
        catch { /* guardar configuración no es crítico */ }
    }
}
