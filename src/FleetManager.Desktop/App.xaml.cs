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
        // Data and provisioning
        services.AddSingleton<IDashboardDataService, DashboardDataService>();
        services.AddSingleton<ISshProvisioningService, SshProvisioningService>();
        services.AddSingleton<IDesktopNodeRegistry, DesktopNodeRegistry>();
        services.AddSingleton<ISshTunnelManager, SshTunnelManager>();
        services.AddSingleton<IDesktopSelfHealingService, DesktopSelfHealingService>();

        // ViewModel scoped so each window gets its own instance if needed
        services.AddTransient<MainWindowViewModel>();

        // Main window itself (receives ViewModel via constructor)
        services.AddTransient<MainWindow>();

        // Node registry management window
        services.AddTransient<NodeRegistryWindow>();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_serviceProvider is not null)
        {
            try
            {
                var selfHealingService = _serviceProvider.GetService<IDesktopSelfHealingService>();
                if (selfHealingService is not null)
                {
                    selfHealingService.StopAsync().GetAwaiter().GetResult();
                    selfHealingService.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }
            }
            catch
            {
            }

            try
            {
                var tunnelManager = _serviceProvider.GetService<ISshTunnelManager>();
                if (tunnelManager is not null)
                {
                    tunnelManager.CloseAllAsync().GetAwaiter().GetResult();
                    tunnelManager.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }
            }
            catch
            {
            }

            _serviceProvider.Dispose();
        }

        base.OnExit(e);
    }
}
