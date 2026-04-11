using FleetManager.Application.Abstractions;
using FleetManager.Application.Services;
using FleetManager.Infrastructure.Persistence;
using FleetManager.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FleetManager.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection") 
            ?? "Host=localhost;Database=FleetManagerDb;Username=postgres;Password=postgres";
            
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(connectionString));

        services.AddScoped<INodeRepository, NodeRepository>();
        services.AddScoped<IAccountRepository, AccountRepository>();
        services.AddScoped<INodeService, NodeService>();
        services.AddScoped<IAccountService, AccountService>();
        return services;
    }

    public static async Task SeedDemoDataAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await AppDbContextSeed.SeedAsync(dbContext);
    }
}
