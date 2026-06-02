using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tabkit.App.Views.Pages;
using Tabkit.App.Views.Windows;

namespace Tabkit.App.Services;

/// <summary>
/// Show MainWindow on app start and navigate to the Audit page by default.
/// </summary>
public sealed class ApplicationHostService(IServiceProvider services) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            var mainWindow = services.GetRequiredService<MainWindow>();
            mainWindow.Show();

            var nav = services.GetRequiredService<INavigationService>();
            nav.Navigate(typeof(AuditPage));
        });

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
