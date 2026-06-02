using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tabkit.App.Services;
using Tabkit.App.ViewModels.Pages;
using Tabkit.App.ViewModels.Windows;
using Tabkit.App.Views.Pages;
using Tabkit.App.Views.Windows;
using Wpf.Ui.DependencyInjection;

namespace Tabkit.App;

public partial class App : Application
{
    private IHost? _host;

    public static IServiceProvider Services => ((App)Current)._host!.Services;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices((_, services) =>
            {
                // WPF-UI infrastructure
                services.AddNavigationViewPageProvider();
                services.AddSingleton<INavigationService, NavigationService>();
                services.AddSingleton<ISnackbarService, SnackbarService>();
                services.AddSingleton<IContentDialogService, ContentDialogService>();

                // Settings live above engine wrappers because AuditService
                // depends on the current GovernanceConfig at construction time.
                services.AddSingleton<SettingsService>();

                // Engine wrappers
                services.AddSingleton<WorkbookService>();
                services.AddSingleton<AuditService>();
                services.AddSingleton<InventoryService>();
                services.AddSingleton<ExtractService>();

                // Windows
                services.AddSingleton<MainWindow>();
                services.AddSingleton<MainWindowViewModel>();

                // Pages
                services.AddSingleton<AuditPage>();
                services.AddSingleton<AuditViewModel>();
                services.AddSingleton<InventoryPage>();
                services.AddSingleton<InventoryViewModel>();
                services.AddSingleton<ExtractPage>();
                services.AddSingleton<ExtractViewModel>();
                services.AddSingleton<SettingsPage>();
                services.AddSingleton<SettingsViewModel>();

                // Hosted bootstrap
                services.AddHostedService<ApplicationHostService>();
            })
            .Build();

        await _host.StartAsync();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
        base.OnExit(e);
    }
}
