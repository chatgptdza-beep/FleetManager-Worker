using FleetManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FleetManager.Infrastructure.Persistence;

public static class AppDbContextSeed
{
    public static async Task SeedAsync(AppDbContext dbContext, CancellationToken cancellationToken = default)
    {
        await dbContext.Database.EnsureCreatedAsync(cancellationToken);
        // No demo data — nodes and accounts are created by the operator via the UI.
    }
}
