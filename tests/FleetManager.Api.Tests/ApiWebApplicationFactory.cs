using FleetManager.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FleetManager.Api.Tests;

public sealed class ApiWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"fleetmanager-tests-{Guid.NewGuid():N}";
    private const string TestConnectionString = "Host=localhost;Database=FleetManagerTests;Username=postgres;Password=postgres";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", TestConnectionString);
        Environment.SetEnvironmentVariable("Jwt__Key", "12345678901234567890123456789012");
        Environment.SetEnvironmentVariable("AdminPassword", "Admin@FleetMgr2026!");
        Environment.SetEnvironmentVariable("AgentApiKey", "TEST-AGENT-KEY");

        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:DefaultConnection", TestConnectionString);
        builder.UseSetting("Jwt:Key", "12345678901234567890123456789012");
        builder.UseSetting("AdminPassword", "Admin@FleetMgr2026!");
        builder.UseSetting("AgentApiKey", "TEST-AGENT-KEY");
        builder.UseSetting("DisableRateLimiting", "true");
        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DisableRateLimiting"] = "true",
                ["ASPNETCORE_ENVIRONMENT"] = "Development",
                ["ConnectionStrings:DefaultConnection"] = TestConnectionString,
                ["Jwt:Key"] = "12345678901234567890123456789012",
                ["AdminPassword"] = "Admin@FleetMgr2026!",
                ["AgentApiKey"] = "TEST-AGENT-KEY"
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<AppDbContext>();
            services.RemoveAll<DbContextOptions<AppDbContext>>();

            services.AddDbContext<AppDbContext>(options =>
                options.UseInMemoryDatabase(_databaseName));
        });
    }
}
