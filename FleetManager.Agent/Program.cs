using FleetManager.Agent;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<AgentSettings>(
    builder.Configuration.GetSection(AgentSettings.SectionName));

builder.Services.AddHttpClient<Worker>(client =>
{
    var baseUrl = builder.Configuration[$"{AgentSettings.SectionName}:{nameof(AgentSettings.BackendBaseUrl)}"];
    if (!string.IsNullOrWhiteSpace(baseUrl))
        client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
});

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
