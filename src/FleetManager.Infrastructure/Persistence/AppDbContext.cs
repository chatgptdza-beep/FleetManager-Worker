using FleetManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FleetManager.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<VpsNode> VpsNodes => Set<VpsNode>();
    public DbSet<AgentInstallJob> AgentInstallJobs => Set<AgentInstallJob>();
    public DbSet<NodeCommand> NodeCommands => Set<NodeCommand>();
    public DbSet<NodeCapability> NodeCapabilities => Set<NodeCapability>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<AccountWorkflowStage> AccountWorkflowStages => Set<AccountWorkflowStage>();
    public DbSet<AccountAlert> AccountAlerts => Set<AccountAlert>();
    public DbSet<ProxyEntry> ProxyEntries => Set<ProxyEntry>();
    public DbSet<ProxyRotationLog> ProxyRotationLogs => Set<ProxyRotationLog>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<VpsNode>(builder =>
        {
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Name).HasMaxLength(100).IsRequired();
            builder.Property(x => x.IpAddress).HasMaxLength(100).IsRequired();
            builder.Property(x => x.SshUsername).HasMaxLength(100).IsRequired();
            builder.Property(x => x.ConnectionState).HasMaxLength(50).IsRequired();
            builder.Property(x => x.Status).HasConversion<string>();
            builder.HasMany(x => x.Accounts)
                .WithOne(x => x.VpsNode)
                .HasForeignKey(x => x.VpsNodeId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AgentInstallJob>(builder =>
        {
            builder.HasKey(x => x.Id);
            builder.Property(x => x.JobStatus).HasConversion<string>();
            builder.HasOne(x => x.VpsNode)
                .WithMany(x => x.InstallJobs)
                .HasForeignKey(x => x.VpsNodeId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<NodeCommand>(builder =>
        {
            builder.HasKey(x => x.Id);
            builder.Property(x => x.CommandType).HasConversion<string>();
            builder.Property(x => x.Status).HasConversion<string>();
            builder.HasOne(x => x.VpsNode)
                .WithMany(x => x.Commands)
                .HasForeignKey(x => x.VpsNodeId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<NodeCapability>(builder =>
        {
            builder.HasKey(x => x.Id);
        });

        modelBuilder.Entity<Account>(builder =>
        {
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Email).HasMaxLength(200).IsRequired();
            builder.Property(x => x.Username).HasMaxLength(100).IsRequired();
            builder.Property(x => x.CurrentStageCode).HasMaxLength(100).IsRequired();
            builder.Property(x => x.CurrentStageName).HasMaxLength(100).IsRequired();
            builder.Property(x => x.Status).HasConversion<string>();
            builder.HasMany(x => x.WorkflowStages)
                .WithOne(x => x.Account)
                .HasForeignKey(x => x.AccountId);
            builder.HasMany(x => x.Alerts)
                .WithOne(x => x.Account)
                .HasForeignKey(x => x.AccountId);
            builder.HasMany(x => x.Proxies)
                .WithOne(x => x.Account)
                .HasForeignKey(x => x.AccountId);
        });

        modelBuilder.Entity<ProxyEntry>(builder =>
        {
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Host).HasMaxLength(150).IsRequired();
            builder.Property(x => x.Username).HasMaxLength(150);
            builder.Property(x => x.Password).HasMaxLength(150);
        });

        modelBuilder.Entity<ProxyRotationLog>(builder =>
        {
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Reason).HasMaxLength(250);
        });

        modelBuilder.Entity<AccountWorkflowStage>(builder =>
        {
            builder.HasKey(x => x.Id);
            builder.Property(x => x.StageCode).HasMaxLength(100).IsRequired();
            builder.Property(x => x.StageName).HasMaxLength(100).IsRequired();
            builder.Property(x => x.State).HasConversion<string>();
        });

        modelBuilder.Entity<AccountAlert>(builder =>
        {
            builder.HasKey(x => x.Id);
            builder.Property(x => x.StageCode).HasMaxLength(100).IsRequired();
            builder.Property(x => x.StageName).HasMaxLength(100).IsRequired();
            builder.Property(x => x.Severity).HasConversion<string>();
            builder.Property(x => x.Title).HasMaxLength(200).IsRequired();
        });
    }
}
