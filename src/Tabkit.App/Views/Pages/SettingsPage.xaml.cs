using System.Windows.Controls;
using Tabkit.App.ViewModels.Pages;

namespace Tabkit.App.Views.Pages;

public partial class SettingsPage : Page
{
    public SettingsPage(SettingsViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}
