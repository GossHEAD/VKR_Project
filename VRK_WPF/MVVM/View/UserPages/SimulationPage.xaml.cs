using System.Windows.Controls;
using VRK_WPF.MVVM.ViewModel;

namespace VRK_WPF.MVVM.View.UserPages;

public partial class SimulationPage : Page
{
    public SimulationPage()
    {
        InitializeComponent();
    }
    
    private void SimNodesListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.OnNodeSelectionChanged(sender, e);
        }
    }
}