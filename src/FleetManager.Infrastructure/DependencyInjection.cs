using FleetManager.Application.Abstractions;
using FleetManager.Application.Services;
using FleetManager.Infrastructure.Persistence;
using FleetManager.Domain.Entities;
using FleetManager.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FleetManager.Infrastructure;

public static class DependencyInjection
{
    private static readonly Guid[] DemoNodeIds =
    {
        Guid.Parse("3a5ff57d-e3d8-4d04-858e-fcef5b4997bf"),
        Guid.Parse("df8ec7ab-4bd8-43c8-bd6d-4b5ebf901437"),
        Guid.Parse("70c8c145-a615-42eb-82bf-b93112f0fe12"),
        Guid.Parse("2f4a72af-4f7b-4c51-a3cb-b0ad6e3b3ecf")
    };

    private static readonly Guid[] DemoAccountIds =
    {
        Guid.Parse("f58b7535-64fc-4569-bd57-c5eecc357f40"),
        Guid.Parse("2bc0f75d-5df9-4fe9-96a0-67f8c4d8dc72"),
        Guid.Parse("8eaa4352-f5a4-49de-9218-25a624cf96af"),
        Guid.Parse("ab0cba77-63a0-4fef-bca0-738f08d2dc55")
    };

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

    public static async Task PurgeDemoDataAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await dbContext.Database.EnsureCreatedAsync();

        var demoAccountIds = await dbContext.Accounts
            .Where(a => DemoAccountIds.Contains(a.Id))
            .Select(a => a.Id)
            .ToArrayAsync();

        if (demoAccountIds.Length > 0)
        {
            var workflowStages = dbContext.AccountWorkflowStages.Where(x => demoAccountIds.Contains(x.AccountId));
            var alerts = dbContext.AccountAlerts.Where(x => demoAccountIds.Contains(x.AccountId));
            var proxies = dbContext.ProxyEntries.Where(x => demoAccountIds.Contains(x.AccountId));
            var rotationLogs = dbContext.ProxyRotationLogs.Where(x => demoAccountIds.Contains(x.AccountId));
            var accounts = dbContext.Accounts.Where(x => demoAccountIds.Contains(x.Id));

            dbContext.ProxyRotationLogs.RemoveRange(rotationLogs);
            dbContext.ProxyEntries.RemoveRange(proxies);
            dbContext.AccountAlerts.RemoveRange(alerts);
            dbContext.AccountWorkflowStages.RemoveRange(workflowStages);
            dbContext.Accounts.RemoveRange(accounts);
        }

        var capabilities = dbContext.NodeCapabilities.Where(x => DemoNodeIds.Contains(x.VpsNodeId));
        var commands = dbContext.NodeCommands.Where(x => DemoNodeIds.Contains(x.VpsNodeId));
        var installJobs = dbContext.AgentInstallJobs.Where(x => DemoNodeIds.Contains(x.VpsNodeId));
        var nodes = dbContext.VpsNodes.Where(x => DemoNodeIds.Contains(x.Id));

        dbContext.NodeCapabilities.RemoveRange(capabilities);
        dbContext.NodeCommands.RemoveRange(commands);
        dbContext.AgentInstallJobs.RemoveRange(installJobs);
        dbContext.VpsNodes.RemoveRange(nodes);

        await dbContext.SaveChangesAsync();
    }
}
