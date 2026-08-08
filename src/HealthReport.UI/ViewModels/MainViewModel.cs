using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HealthReport.AI.Pipeline;
using HealthReport.AI.Services;
using HealthReport.Core.Aggregation;
using HealthReport.Core.Export;
using HealthReport.Core.Models;
using HealthReport.Core.Parsing;
using HealthReport.UI.Services;

namespace HealthReport.UI.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    // --- Servicios ---
    private readonly IHealthParser _parser = new AppleHealthXmlParser();
    private readonly IHealthAggregator _aggregator = new HealthAggregator();
    private readonly IReportExporter _exporter = new MarkdownReportExporter();
    private readonly DialogService _dialogs = new();
    private readonly AppConfig _config;

    private OllamaClient? _ollamaClient;
    private CancellationTokenSource? _cts;
    private IReadOnlyList<AnalysisResult>? _lastResults;
    private List<HealthRecord>? _lastRecords;  // guardamos para los gráficos

    public MainViewModel()
    {
        _config = AppConfig.Load();
        OllamaUrl = _config.OllamaUrl;
        AnalysisDays = _config.AnalysisDays;

        // Inicializar las 4 fases en el panel lateral
        Phases =
        [
            new PhaseViewModel { Name = "Fase 1 · Perfil demográfico" },
            new PhaseViewModel { Name = "Fase 2 · Actividad física" },
            new PhaseViewModel { Name = "Fase 3 · Cardiovascular y sueño" },
            new PhaseViewModel { Name = "Fase 4 · Síntesis final" }
        ];
    }

    // --- Propiedades observables ---

    [ObservableProperty] private string _zipFilePath = string.Empty;
    [ObservableProperty] private string _ollamaUrl = "http://localhost:11434";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStartAnalysis))]
    private string _selectedModel = string.Empty;

    [ObservableProperty] private ObservableCollection<string> _availableModels = [];
    [ObservableProperty] private int _analysisDays = 90;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStartAnalysis))]
    [NotifyCanExecuteChangedFor(nameof(StartAnalysisCommand))]
    private bool _isBusy;

    [ObservableProperty] private string _statusMessage = "Selecciona un archivo ZIP para comenzar.";
    [ObservableProperty] private double _progressValue;
    [ObservableProperty] private string _analysisText = string.Empty;
    [ObservableProperty] private bool _analysisComplete;
    [ObservableProperty] private bool _hasError;
    [ObservableProperty] private string _errorDetail = string.Empty;

    /// <summary>Panel lateral: una entrada por fase.</summary>
    public ObservableCollection<PhaseViewModel> Phases { get; }

    /// <summary>ViewModel del panel de gráficos.</summary>
    public ChartsViewModel Charts { get; } = new();

    /// <summary>Fase actualmente seleccionada en el panel lateral (para ver su texto).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedPhaseContent))]
    private PhaseViewModel? _selectedPhase;

    public string SelectedPhaseContent => SelectedPhase?.Content ?? AnalysisText;

    public bool CanStartAnalysis =>
        !IsBusy &&
        !string.IsNullOrEmpty(SelectedModel) &&
        !string.IsNullOrEmpty(ZipFilePath) &&
        File.Exists(ZipFilePath);

    partial void OnZipFilePathChanged(string value) => OnPropertyChanged(nameof(CanStartAnalysis));
    partial void OnSelectedModelChanged(string value) => OnPropertyChanged(nameof(CanStartAnalysis));

    // --- Comandos ---

    [RelayCommand]
    private void SelectZipFile()
    {
        var path = _dialogs.OpenZipFile(_config.LastZipFolder);
        if (path is null) return;

        ZipFilePath = path;
        _config.LastZipFolder = Path.GetDirectoryName(path) ?? string.Empty;
        _config.Save();
        StatusMessage = $"Archivo: {Path.GetFileName(path)}";
        OnPropertyChanged(nameof(CanStartAnalysis));
        StartAnalysisCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private async Task LoadModelsAsync()
    {
        IsBusy = true;
        StatusMessage = "Conectando con Ollama...";
        AvailableModels.Clear();
        HasError = false;

        try
        {
            _ollamaClient?.Dispose();
            _ollamaClient = new OllamaClient(OllamaUrl);
            var models = await _ollamaClient.GetAvailableModelsAsync();

            foreach (var m in models)
                AvailableModels.Add(m);

            if (AvailableModels.Count > 0)
            {
                // Restaurar último modelo usado si está disponible
                SelectedModel = AvailableModels.Contains(_config.LastModel)
                    ? _config.LastModel
                    : AvailableModels[0];
                StatusMessage = $"{AvailableModels.Count} modelo(s) disponible(s). Modelo: {SelectedModel}";
            }
            else
            {
                StatusMessage = "No se encontraron modelos. ¿Está Ollama en ejecución?";
            }

            _config.OllamaUrl = OllamaUrl;
            _config.Save();
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorDetail = ex.Message;
            StatusMessage = $"Error al conectar con Ollama: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            StartAnalysisCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(CanStartAnalysis))]
    private async Task StartAnalysisAsync()
    {
        if (_ollamaClient is null)
        {
            StatusMessage = "Primero conecta con Ollama y selecciona un modelo.";
            return;
        }

        _cts = new CancellationTokenSource();
        IsBusy = true;
        AnalysisComplete = false;
        HasError = false;
        AnalysisText = string.Empty;
        ProgressValue = 0;
        _lastResults = null;
        _lastRecords = null;

        // Resetear fases
        foreach (var p in Phases)
        {
            p.Status = PhaseStatus.Waiting;
            p.Content = string.Empty;
            p.ErrorMessage = string.Empty;
        }
        SelectedPhase = null;

        // Guardar preferencias
        _config.LastModel = SelectedModel;
        _config.AnalysisDays = AnalysisDays;
        _config.Save();

        string? tempFile = null;

        try
        {
            // Paso 1: extraer export.xml a archivo temporal (soporta ZIPs de 500MB+)
            StatusMessage = "Extrayendo export.xml del archivo ZIP...";
            tempFile = await ExtractToTempFileAsync(ZipFilePath, _cts.Token);
            ProgressValue = 5;

            // Paso 2: parsear XML en streaming
            StatusMessage = "Parseando datos de salud...";
            var parseProgress = new Progress<int>(count =>
                StatusMessage = $"Parseando... {count:N0} nodos procesados");

            await using var xmlStream = File.OpenRead(tempFile);
            var (profile, records, workouts) = await _parser.ParseAsync(xmlStream, parseProgress, _cts.Token);
            _lastRecords = records;

            StatusMessage = $"Leídos {records.Count:N0} registros y {workouts.Count} entrenamientos.";
            ProgressValue = 25;

            // Paso 3: agregar datos
            StatusMessage = "Calculando estadísticas...";
            var summary = _aggregator.Aggregate(profile, records, workouts, AnalysisDays);
            ProgressValue = 30;

            if (summary.Metrics.Count == 0)
            {
                StatusMessage = "⚠️ No se encontraron métricas de salud en el archivo. Verifica que es un export válido de Apple Health.";
                HasError = true;
                return;
            }

            // Paso 4: pipeline de IA
            var pipeline = new AnalysisPipeline(_ollamaClient);
            int phaseIndex = -1;
            double phaseStep = 65.0 / 4;

            var phaseBuilders = Phases.Select(_ => new StringBuilder()).ToArray();

            _lastResults = await pipeline.RunAsync(
                summary,
                SelectedModel,
                onToken: async (phase, token) =>
                {
                    int idx = (int)phase - 1;
                    phaseBuilders[idx].Append(token);
                    Phases[idx].Content = phaseBuilders[idx].ToString();

                    // El texto global muestra la fase activa
                    if (SelectedPhase is null || SelectedPhase == Phases[idx])
                        AnalysisText = Phases[idx].Content;

                    await Task.CompletedTask;
                },
                onPhaseComplete: async result =>
                {
                    phaseIndex++;
                    ProgressValue = 30 + (phaseIndex + 1) * phaseStep;

                    var phaseVm = Phases[phaseIndex];
                    phaseVm.Status = result.IsSuccess ? PhaseStatus.Done : PhaseStatus.Failed;
                    if (!result.IsSuccess)
                        phaseVm.ErrorMessage = result.ErrorMessage ?? string.Empty;

                    StatusMessage = result.IsSuccess
                        ? $"✓ {result.PhaseName} completada."
                        : $"⚠ {result.PhaseName}: {result.ErrorMessage}";

                    // Auto-seleccionar la última fase completada para mostrar su texto
                    SelectedPhase = phaseVm;
                    await Task.CompletedTask;
                },
                _cts.Token);

            // Marcar como completado
            ProgressValue = 100;
            AnalysisComplete = true;

            // Generar series temporales para los gráficos
            var timeSeries = _aggregator.GetTimeSeries(records, ChartsViewModel.ChartableMetrics, AnalysisDays);
            Charts.LoadSeries(timeSeries);

            var successCount = _lastResults.Count(r => r.IsSuccess);
            StatusMessage = $"✅ Análisis completado ({successCount}/4 fases correctas). Puedes guardar el informe.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Análisis cancelado por el usuario.";
            foreach (var p in Phases.Where(p => p.Status == PhaseStatus.Running))
                p.Status = PhaseStatus.Failed;
        }
        catch (FileNotFoundException ex)
        {
            HasError = true;
            ErrorDetail = ex.Message;
            StatusMessage = $"❌ Archivo no encontrado: {ex.Message}";
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorDetail = ex.ToString();
            StatusMessage = $"❌ Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            StartAnalysisCommand.NotifyCanExecuteChanged();

            // Eliminar archivo temporal
            if (tempFile is not null && File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [RelayCommand]
    private void CancelAnalysis()
    {
        _cts?.Cancel();
        StatusMessage = "Cancelando análisis...";
    }

    [RelayCommand]
    private async Task SaveReportAsync()
    {
        if (_lastResults is null) return;

        var path = _dialogs.SaveMarkdownFile(_config.LastOutputFolder);
        if (path is null) return;

        try
        {
            await _exporter.ExportAsync(_lastResults, path);
            _config.LastOutputFolder = Path.GetDirectoryName(path) ?? string.Empty;
            _config.Save();
            StatusMessage = $"✅ Informe Markdown guardado en: {path}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Error al guardar: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SavePdfAsync()
    {
        if (_lastResults is null) return;

        var path = _dialogs.SavePdfFile(_config.LastOutputFolder);
        if (path is null) return;

        try
        {
            StatusMessage = "Generando PDF...";
            var pdfExporter = new HealthReport.Core.Export.PdfReportExporter();
            await pdfExporter.ExportAsync(_lastResults, path);
            _config.LastOutputFolder = Path.GetDirectoryName(path) ?? string.Empty;
            _config.Save();
            StatusMessage = $"✅ Informe PDF guardado en: {path}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Error al generar PDF: {ex.Message}";
        }
    }

    [RelayCommand]
    private void SelectPhase(PhaseViewModel? phase)
    {
        SelectedPhase = phase;
        if (phase is not null)
            AnalysisText = phase.Content;
    }

    // --- Helpers ---

    /// <summary>
    /// Extrae export.xml del ZIP a un archivo temporal para no cargar 500MB en RAM.
    /// </summary>
    private static async Task<string> ExtractToTempFileAsync(string zipPath, CancellationToken ct)
    {
        using var zip = ZipFile.OpenRead(zipPath);

        var entry = zip.Entries.FirstOrDefault(e =>
            e.FullName.EndsWith("export.xml", StringComparison.OrdinalIgnoreCase));

        if (entry is null)
            throw new FileNotFoundException(
                "No se encontró el archivo export.xml dentro del ZIP. " +
                "Asegúrate de que es el archivo exportado directamente desde la app Salud de Apple.");

        var tempPath = Path.Combine(Path.GetTempPath(), $"healthreport_{Guid.NewGuid():N}.xml");

        await using var entryStream = entry.Open();
        await using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write,
            FileShare.None, bufferSize: 81920, useAsync: true);

        await entryStream.CopyToAsync(fileStream, ct).ConfigureAwait(false);
        return tempPath;
    }
}
