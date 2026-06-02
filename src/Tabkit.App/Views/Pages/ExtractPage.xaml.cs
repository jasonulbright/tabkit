using System.Windows.Controls;
using Tabkit.App.ViewModels.Pages;

namespace Tabkit.App.Views.Pages;

public partial class ExtractPage : Page
{
    public ExtractPage(ExtractViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }

    private ExtractViewModel? ViewModel => DataContext as ExtractViewModel;

    private void OnRecentSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox cb) return;
        if (cb.SelectedItem is not string path) return;
        cb.SelectedIndex = -1;
        if (ViewModel?.OpenRecentCommand.CanExecute(path) == true)
            ViewModel.OpenRecentCommand.Execute(path);
    }
}
