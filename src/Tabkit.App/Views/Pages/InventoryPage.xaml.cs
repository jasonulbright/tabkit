using System.Windows.Controls;
using Tabkit.App.ViewModels.Pages;

namespace Tabkit.App.Views.Pages;

public partial class InventoryPage : Page
{
    public InventoryPage(InventoryViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}
