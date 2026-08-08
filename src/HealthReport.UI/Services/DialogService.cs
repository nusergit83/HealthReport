using System.IO;
using Microsoft.Win32;

namespace HealthReport.UI.Services;

/// <summary>
/// Abstrae los diálogos del sistema operativo para facilitar los tests y mantener el ViewModel limpio.
/// </summary>
public sealed class DialogService
{
    public string? OpenZipFile(string initialFolder = "")
    {
        var dialog = new OpenFileDialog
        {
            Title = "Selecciona el export de Apple Health",
            Filter = "Archivo ZIP (*.zip)|*.zip",
            CheckFileExists = true,
            InitialDirectory = Directory.Exists(initialFolder) ? initialFolder : string.Empty
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? SaveMarkdownFile(string initialFolder = "")
    {
        var dialog = new SaveFileDialog
        {
            Title = "Guardar informe de salud",
            Filter = "Markdown (*.md)|*.md|Texto (*.txt)|*.txt",
            FileName = $"HealthReport_{DateTime.Now:yyyyMMdd_HHmm}.md",
            InitialDirectory = Directory.Exists(initialFolder) ? initialFolder : string.Empty
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? SavePdfFile(string initialFolder = "")
    {
        var dialog = new SaveFileDialog
        {
            Title = "Guardar informe PDF",
            Filter = "PDF (*.pdf)|*.pdf",
            FileName = $"HealthReport_{DateTime.Now:yyyyMMdd_HHmm}.pdf",
            InitialDirectory = Directory.Exists(initialFolder) ? initialFolder : string.Empty
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
