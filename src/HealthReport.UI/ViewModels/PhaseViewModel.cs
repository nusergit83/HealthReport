using CommunityToolkit.Mvvm.ComponentModel;

namespace HealthReport.UI.ViewModels;

public enum PhaseStatus { Waiting, Running, Done, Failed }

/// <summary>
/// Estado observable de una fase de análisis, para mostrar en el panel lateral de la UI.
/// </summary>
public sealed partial class PhaseViewModel : ObservableObject
{
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private PhaseStatus _status = PhaseStatus.Waiting;
    [ObservableProperty] private string _content = string.Empty;
    [ObservableProperty] private string _errorMessage = string.Empty;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage) && Status == PhaseStatus.Failed;

    public string StatusIcon => Status switch
    {
        PhaseStatus.Waiting => "⏳",
        PhaseStatus.Running => "⚙️",
        PhaseStatus.Done => "✅",
        PhaseStatus.Failed => "❌",
        _ => "?"
    };

    public string StatusColor => Status switch
    {
        PhaseStatus.Waiting => "#999999",
        PhaseStatus.Running => "#0078D4",
        PhaseStatus.Done => "#107C10",
        PhaseStatus.Failed => "#C50F1F",
        _ => "#333"
    };

    partial void OnStatusChanged(PhaseStatus value)
    {
        OnPropertyChanged(nameof(StatusIcon));
        OnPropertyChanged(nameof(StatusColor));
        OnPropertyChanged(nameof(HasError));
    }

    partial void OnErrorMessageChanged(string value) => OnPropertyChanged(nameof(HasError));
}
