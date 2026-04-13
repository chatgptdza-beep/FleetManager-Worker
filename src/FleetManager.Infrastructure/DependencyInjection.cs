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
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration, string? environmentName = null)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            environmentName ??= configuration["DOTNET_ENVIRONMENT"]
                ?? configuration["ASPNETCORE_ENVIRONMENT"]
                ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
                ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");

            if (string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase))
            {
                connectionString = "Host=localhost;Database=FleetManagerDb;Username=postgres;Password=postgres";
            }
            else
            {
                throw new InvalidOperationException("Missing connection string 'DefaultConnection'.");
            }
        }
            
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(connectionString));

        services.AddScoped<INodeRepository, NodeRepository>();
        services.AddScoped<IAccountRepository, AccountRepository>();
        services.AddScoped<INodeService, NodeService>();
        services.AddScoped<IAccountService, AccountService>();
        return services;
    }

    public static async Task MigrateAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (dbContext.Database.IsRelational())
        {
            await dbContext.Database.MigrateAsync();
        }
        else
        {
            await dbContext.Database.EnsureCreatedAsync();
        }
    }
}
