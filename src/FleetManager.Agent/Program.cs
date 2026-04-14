using FleetManager.Agent;
using FleetManager.Agent.Options;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.Configure<AgentOptions>(builder.Configuration.GetSection(AgentOptions.SectionName));
builder.Services.AddHttpClient("AgentClient")
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5)
    })
    .AddStandardResilienceHandler(options =>
    {
        options.Retry.MaxRetryAttempts = 3;
        options.Retry.Delay = TimeSpan.FromSeconds(2);
        options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(10);
        options.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(2);
    });

// Docker container monitor is optional — enable via Agent:EnableDockerMonitor in appsettings
var agentConfig = builder.Configuration.GetSection(AgentOptions.SectionName);
if (agentConfig.GetValue<bool>("EnableDockerMonitor"))
{
    builder.Services.AddSingleton<FleetManager.Agent.Services.DockerOrchestrator>();
    builder.Services.AddHostedService<FleetManager.Agent.Services.ContainerMonitor>();
}

builder.Services.AddSingleton<FleetManager.Agent.Services.LinuxMetricsCollector>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
