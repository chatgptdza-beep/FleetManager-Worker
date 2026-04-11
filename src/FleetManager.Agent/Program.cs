using FleetManager.Agent;
using FleetManager.Agent.Options;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.Configure<AgentOptions>(builder.Configuration.GetSection(AgentOptions.SectionName));
builder.Services.AddHttpClient();
builder.Services.AddSingleton<FleetManager.Agent.Services.DockerOrchestrator>();
builder.Services.AddSingleton<FleetManager.Agent.Services.LinuxMetricsCollector>();
builder.Services.AddHostedService<FleetManager.Agent.Services.ContainerMonitor>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
