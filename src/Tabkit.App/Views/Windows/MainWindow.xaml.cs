using Tabkit.App.ViewModels.Windows;

namespace Tabkit.App.Views.Windows;

public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
{
    public MainWindow(
        MainWindowViewModel viewModel,
        INavigationService navigationService,
        ISnackbarService snackbarService,
        IContentDialogService contentDialogService)
    {
        DataContext = viewModel;
        InitializeComponent();

        navigationService.SetNavigationControl(NavigationView);
        snackbarService.SetSnackbarPresenter(SnackbarPresenter);
        contentDialogService.SetDialogHost(RootContentDialog);
    }
}
