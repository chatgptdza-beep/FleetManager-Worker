using System.Windows;
using FleetManager.Desktop.Services;
using FleetManager.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace FleetManager.Desktop;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    private void App_Startup(object sender, StartupEventArgs e)
    {
        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Data & provisioning
        services.AddSingleton<IDashboardDataService, DashboardDataService>();
        services.AddSingleton<ISshProvisioningService, SshProvisioningService>();

        // ViewModel — scoped so each window gets its own instance if needed
        services.AddTransient<MainWindowViewModel>();

        // Main window itself (receives ViewModel via constructor)
        services.AddTransient<MainWindow>();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }
}
