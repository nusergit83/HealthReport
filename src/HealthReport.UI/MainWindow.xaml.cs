using System.Windows;
using System.Windows.Controls;
using HealthReport.UI.ViewModels;

namespace HealthReport.UI;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        if (DataContext is MainViewModel vm)
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MainViewModel.AnalysisText))
                    Dispatcher.InvokeAsync(() => AnalysisScroll.ScrollToEnd());
            };
    }

    private void PhaseList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is MainViewModel vm && sender is ListBox lb)
            vm.SelectPhaseCommand.Execute(lb.SelectedItem as PhaseViewModel);
    }
}
