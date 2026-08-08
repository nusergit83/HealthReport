using HealthReport.Core.Models;
using Markdig;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;

namespace HealthReport.Core.Export;

/// <summary>
/// Exporta el informe de salud a PDF usando PdfSharp 6.
/// Incluye un FontResolver que lee las fuentes directamente del sistema de archivos de Windows,
/// requisito de PdfSharp 6 en .NET cuando no se usa GDI+.
/// </summary>
public sealed class PdfReportExporter : IReportExporter
{
    private const double MarginPt = 50;
    private const double PageWidthPt = 595;
    private const double PageHeightPt = 842;
    private const double ContentWidth = PageWidthPt - 2 * MarginPt;

    public async Task ExportAsync(
        IEnumerable<AnalysisResult> phases,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        if (!outputPath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            outputPath = Path.ChangeExtension(outputPath, ".pdf");

        var lines = BuildLines(phases);
        await Task.Run(() =>
        {
            EnsureFontResolver();
            RenderPdf(lines, outputPath);
        }, cancellationToken);
    }

    private static void EnsureFontResolver()
    {
        if (GlobalFontSettings.FontResolver is WindowsFontResolver) return;
        GlobalFontSettings.FontResolver = new WindowsFontResolver();
    }

    private static List<(string Text, bool IsHeading, bool IsSubheading)> BuildLines(IEnumerable<AnalysisResult> phases)
    {
        var result = new List<(string, bool, bool)>();
        result.Add(($"Informe de Salud -- {DateTime.Now:dd/MM/yyyy HH:mm}", true, false));
        result.Add((string.Empty, false, false));

        foreach (var phase in phases)
        {
            result.Add((phase.PhaseName, false, true));
            result.Add((string.Empty, false, false));

            if (phase.IsSuccess)
            {
                var plainText = Markdown.ToPlainText(phase.Content ?? string.Empty).Trim();
                foreach (var line in plainText.Split('\n'))
                    result.Add((line.TrimEnd(), false, false));
            }
            else
            {
                result.Add(($"Advertencia: Error en esta fase: {phase.ErrorMessage}", false, false));
            }

            result.Add((string.Empty, false, false));
            result.Add(("------------------------------------------------", false, false));
            result.Add((string.Empty, false, false));
        }

        result.Add(($"Generado el {DateTime.Now:F}", false, false));
        return result;
    }

    private static void RenderPdf(List<(string Text, bool IsHeading, bool IsSubheading)> lines, string outputPath)
    {
        using var document = new PdfDocument();
        document.Info.Title = "HealthReport";
        document.Info.Author = "HealthReport App";

        var headingFont = new XFont("Arial", 16, XFontStyleEx.Bold);
        var subheadingFont = new XFont("Arial", 13, XFontStyleEx.Bold);
        var bodyFont = new XFont("Arial", 10, XFontStyleEx.Regular);

        PdfPage? page = null;
        XGraphics? gfx = null;
        double y = MarginPt;

        void NewPage()
        {
            gfx?.Dispose();
            page = document.AddPage();
            page.Width = XUnitPt.FromPoint(PageWidthPt);
            page.Height = XUnitPt.FromPoint(PageHeightPt);
            gfx = XGraphics.FromPdfPage(page);
            y = MarginPt;
        }

        NewPage();

        foreach (var (text, isHeading, isSubheading) in lines)
        {
            var font = isHeading ? headingFont : isSubheading ? subheadingFont : bodyFont;
            var lineHeight = isHeading ? 24.0 : isSubheading ? 18.0 : 14.0;
            var color = isHeading ? XBrushes.DarkBlue : isSubheading ? XBrushes.DarkSlateBlue : XBrushes.Black;

            if (string.IsNullOrWhiteSpace(text))
            {
                y += lineHeight / 2.0;
                continue;
            }

            var wrappedLines = WrapText(gfx!, text, font, ContentWidth);
            foreach (var wl in wrappedLines)
            {
                if (y + lineHeight + 2 > PageHeightPt - MarginPt)
                    NewPage();

                gfx!.DrawString(wl, font, color,
                    new XRect(MarginPt, y, ContentWidth, lineHeight + 4),
                    XStringFormats.TopLeft);
                y += lineHeight;
            }
        }

        gfx?.Dispose();
        document.Save(outputPath);
    }

    private static List<string> WrapText(XGraphics gfx, string text, XFont font, double maxWidth)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var lines = new List<string>();
        var current = string.Empty;

        foreach (var word in words)
        {
            var test = current.Length == 0 ? word : current + " " + word;
            if (gfx.MeasureString(test, font).Width > maxWidth && current.Length > 0)
            {
                lines.Add(current);
                current = word;
            }
            else
            {
                current = test;
            }
        }

        if (current.Length > 0) lines.Add(current);
        return lines.Count > 0 ? lines : [text];
    }
}

/// <summary>
/// Resuelve fuentes leyendo los archivos .ttf directamente de la carpeta Fonts de Windows.
/// Necesario porque PdfSharp 6 en .NET no tiene acceso a GDI+ para enumerar fuentes del sistema.
/// </summary>
internal sealed class WindowsFontResolver : IFontResolver
{
    private static readonly string FontsFolder =
        Environment.GetFolderPath(Environment.SpecialFolder.Fonts);

    private static readonly Dictionary<string, string> FontMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Arial#400"]  = "arial.ttf",
        ["Arial#700"]  = "arialbd.ttf",
        ["Arial#400I"] = "ariali.ttf",
        ["Arial#700I"] = "arialbi.ttf",
    };

    public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
    {
        var style = (isBold ? 700 : 400).ToString() + (isItalic ? "I" : "");
        var key = $"{familyName}#{style}";

        if (FontMap.ContainsKey(key))
            return new FontResolverInfo(key);

        // Fallback a regular
        var baseKey = $"{familyName}#400";
        if (FontMap.ContainsKey(baseKey))
            return new FontResolverInfo(baseKey);

        return null;
    }

    public byte[]? GetFont(string faceName)
    {
        if (!FontMap.TryGetValue(faceName, out var fileName))
            return null;

        var path = Path.Combine(FontsFolder, fileName);
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }
}
