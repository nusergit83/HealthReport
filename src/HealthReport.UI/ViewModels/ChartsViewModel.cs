using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HealthReport.Core.Aggregation;
using HealthReport.Core.Models;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;

namespace HealthReport.UI.ViewModels;

/// <summary>
/// ViewModel del panel de gráficos. Genera modelos OxyPlot a partir de los datos de salud.
/// </summary>
public sealed partial class ChartsViewModel : ObservableObject
{
    // Métricas disponibles para graficar
    public static readonly string[] ChartableMetrics =
    [
        "HKQuantityTypeIdentifierStepCount",
        "HKQuantityTypeIdentifierRestingHeartRate",
        "HKQuantityTypeIdentifierHeartRateVariabilitySDNN",
        "HKQuantityTypeIdentifierActiveEnergyBurned",
        "HKQuantityTypeIdentifierBodyMass",
        "HKQuantityTypeIdentifierVO2Max",
        "HKQuantityTypeIdentifierDistanceWalkingRunning",
        "HKQuantityTypeIdentifierOxygenSaturation",
    ];

    [ObservableProperty] private PlotModel? _stepsPlot;
    [ObservableProperty] private PlotModel? _heartRatePlot;
    [ObservableProperty] private PlotModel? _hvRplot;
    [ObservableProperty] private PlotModel? _weightPlot;
    [ObservableProperty] private bool _hasData;
    [ObservableProperty] private string _noDataMessage = "El análisis aún no se ha ejecutado.";

    public void LoadSeries(IReadOnlyList<MetricTimeSeries> series)
    {
        HasData = series.Count > 0;
        if (!HasData)
        {
            NoDataMessage = "No se encontraron datos suficientes para generar gráficos.";
            return;
        }

        StepsPlot = BuildPlot(series, "HKQuantityTypeIdentifierStepCount", "Pasos diarios", OxyColors.SteelBlue);
        HeartRatePlot = BuildPlot(series, "HKQuantityTypeIdentifierRestingHeartRate", "FC en reposo (bpm)", OxyColors.Crimson);
        HvRplot = BuildPlot(series, "HKQuantityTypeIdentifierHeartRateVariabilitySDNN", "HRV (ms)", OxyColors.SeaGreen);
        WeightPlot = BuildPlot(series, "HKQuantityTypeIdentifierBodyMass", "Peso (kg)", OxyColors.DarkOrange);
    }

    private static PlotModel? BuildPlot(IReadOnlyList<MetricTimeSeries> series, string metricType, string title, OxyColor color)
    {
        var metric = series.FirstOrDefault(s => s.MetricType == metricType);
        if (metric is null || metric.Points.Count == 0) return null;

        var model = new PlotModel
        {
            Title = title,
            Background = OxyColors.White,
            PlotAreaBorderThickness = new OxyThickness(0, 0, 0, 1),
            TitleFontSize = 13,
            TitleFontWeight = FontWeights.Bold
        };

        model.Axes.Add(new DateTimeAxis
        {
            Position = AxisPosition.Bottom,
            StringFormat = "dd/MM",
            MajorGridlineStyle = LineStyle.Dot,
            MajorGridlineColor = OxyColor.FromRgb(220, 220, 220),
            FontSize = 10
        });

        model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Left,
            MajorGridlineStyle = LineStyle.Dot,
            MajorGridlineColor = OxyColor.FromRgb(220, 220, 220),
            FontSize = 10,
            Title = metric.Unit,
            TitleFontSize = 9
        });

        var lineSeries = new LineSeries
        {
            Color = color,
            StrokeThickness = 2,
            MarkerType = metric.Points.Count <= 30 ? MarkerType.Circle : MarkerType.None,
            MarkerSize = 3,
            MarkerFill = color
        };

        foreach (var pt in metric.Points)
            lineSeries.Points.Add(new DataPoint(
                DateTimeAxis.ToDouble(pt.Date.ToDateTime(TimeOnly.MinValue)),
                pt.Value));

        // Línea de media
        var avg = metric.Average;
        var avgSeries = new LineSeries
        {
            Color = OxyColor.FromAColor(100, color),
            StrokeThickness = 1.5,
            LineStyle = LineStyle.Dash,
            Title = $"Media: {avg:F1} {metric.Unit}"
        };
        if (metric.Points.Count >= 2)
        {
            avgSeries.Points.Add(new DataPoint(
                DateTimeAxis.ToDouble(metric.Points[0].Date.ToDateTime(TimeOnly.MinValue)), avg));
            avgSeries.Points.Add(new DataPoint(
                DateTimeAxis.ToDouble(metric.Points[^1].Date.ToDateTime(TimeOnly.MinValue)), avg));
        }

        model.Series.Add(lineSeries);
        model.Series.Add(avgSeries);

        return model;
    }
}
