using System.Security.Claims;
using System.Text;
using FleetManager.Api.Hubs;
using FleetManager.Api.Services;
using FleetManager.Application.Abstractions;
using FleetManager.Contracts.Agent;
using FleetManager.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.SignalR;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration, builder.Environment.EnvironmentName);
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSignalR();
builder.Services.AddHealthChecks();
builder.Services.AddScoped<IAccountAutomationCoordinator, AccountAutomationCoordinator>();
builder.Services.AddScoped<IWorkerInboxService, WorkerInboxService>();

var jwtSettings = builder.Configuration.GetSection("Jwt");
var jwtIssuer = jwtSettings["Issuer"];
var jwtAudience = jwtSettings["Audience"];
var jwtKey = ResolveSecret(
    builder.Configuration,
    builder.Environment,
    "Jwt:Key",
    fallbackDevelopmentValue: "12345678901234567890123456789012");
var adminPassword = ResolveSecret(
    builder.Configuration,
    builder.Environment,
    "AdminPassword",
    fallbackDevelopmentValue: "Admin@FleetMgr2026!");
var agentApiKey = ResolveSecret(
    builder.Configuration,
    builder.Environment,
    "AgentApiKey",
    fallbackDevelopmentValue: "MASTER-KEY-12345",
    "Agent:ApiKey");
var encodedKey = Encoding.UTF8.GetBytes(jwtKey);
var corsOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>()?
    .Where(static origin => !string.IsNullOrWhiteSpace(origin))
    .ToArray()
    ?? Array.Empty<string>();

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(encodedKey),
        ValidateIssuer = !string.IsNullOrWhiteSpace(jwtIssuer),
        ValidIssuer = jwtIssuer,
        ValidateAudience = !string.IsNullOrWhiteSpace(jwtAudience),
        ValidAudience = jwtAudience,
        ValidateLifetime = true
    };
});

builder.Services.AddAuthorization();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyHeader().AllowAnyMethod();

        if (corsOrigins.Length > 0)
        {
            policy.WithOrigins(corsOrigins).AllowCredentials();
            return;
        }

        if (builder.Environment.IsDevelopment())
        {
            policy.SetIsOriginAllowed(IsLoopbackOrigin).AllowCredentials();
        }
    });
});

var app = builder.Build();

var shouldSeedDemoData = builder.Configuration.GetValue<bool>("SeedDemoData");
if (shouldSeedDemoData)
{
    await app.Services.SeedDemoDataAsync();
}
else
{
    await app.Services.PurgeDemoDataAsync();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}
app.UseAuthentication();

// Machine routes accept either a valid operator JWT or the agent API key.
app.Use(async (context, next) =>
{
    if (RequiresMachineCredential(context.Request.Path))
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            await next(context);
            return;
        }

        var extractedApiKey = context.Request.Headers["X-Api-Key"].ToString().Trim();
        if (!string.Equals(extractedApiKey, agentApiKey, StringComparison.Ordinal))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Unauthorized machine request");
            return;
        }

        context.User = CreateMachinePrincipal();
    }

    await next(context);
});

app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    timeUtc = DateTime.UtcNow
})).AllowAnonymous();

app.MapPost("/api/auth/token", (AuthTokenRequest request) =>
{
    if (request.Password != adminPassword)
    {
        return Results.Unauthorized();
    }

    var claims = new List<Claim>
    {
        new("name", "admin"),
        new(ClaimTypes.Role, "Operator"),
        new("role", "Operator")
    };

    var tokenDescriptor = new Microsoft.IdentityModel.Tokens.SecurityTokenDescriptor
    {
        Subject = new ClaimsIdentity(claims),
        Expires = DateTime.UtcNow.AddHours(24),
        Issuer = jwtIssuer,
        Audience = jwtAudience,
        SigningCredentials = new Microsoft.IdentityModel.Tokens.SigningCredentials(
            new SymmetricSecurityKey(encodedKey),
            SecurityAlgorithms.HmacSha256Signature)
    };

    var tokenHandler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
    var token = tokenHandler.CreateToken(tokenDescriptor);
    return Results.Ok(new { token = tokenHandler.WriteToken(token) });
}).AllowAnonymous();

app.MapControllers();
app.MapHub<OperationsHub>("/hubs/operations");

app.MapPost("/api/agent/heartbeat", async (
    AgentHeartbeatRequest request,
    INodeService nodeService,
    IHubContext<OperationsHub, IOperationsClient> hub,
    CancellationToken cancellationToken) =>
{
    var node = await nodeService.UpdateHeartbeatAsync(
        request.NodeId,
        request.CpuPercent,
        request.RamPercent,
        request.DiskPercent,
        request.RamUsedGb,
        request.StorageUsedGb,
        request.PingMs,
        request.ActiveSessions,
        request.ControlPort,
        request.ConnectionState,
        request.ConnectionTimeoutSeconds,
        request.AgentVersion,
        cancellationToken);

    await hub.Clients.All.SendNodeHeartbeatEvent(
        node);

    return Results.Accepted();
});

app.MapGet("/api/agent/nodes/{nodeId:guid}/commands/next", async (
    Guid nodeId,
    INodeService nodeService,
    CancellationToken cancellationToken) =>
{
    var command = await nodeService.GetNextPendingCommandAsync(nodeId, cancellationToken);
    return command is null ? Results.NoContent() : Results.Ok(command);
});

app.MapPost("/api/agent/commands/{commandId:guid}/complete", async (
    Guid commandId,
    AgentCommandCompletionRequest request,
    INodeService nodeService,
    IWorkerInboxService workerInboxService,
    CancellationToken cancellationToken) =>
{
    await nodeService.CompleteCommandAsync(request.NodeId, commandId, request.Succeeded, request.ResultMessage, cancellationToken);
    if (!request.Succeeded)
    {
        await workerInboxService.RecordCommandFailureAsync(commandId, cancellationToken);
    }
    return Results.Accepted();
});

app.Run();

static string ResolveSecret(
    IConfiguration configuration,
    IHostEnvironment environment,
    string key,
    string? fallbackDevelopmentValue = null,
    params string[] alternateKeys)
{
    var value = configuration[key];
    if (!string.IsNullOrWhiteSpace(value))
    {
        return value.Trim();
    }

    foreach (var alternateKey in alternateKeys)
    {
        value = configuration[alternateKey];
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value.Trim();
        }
    }

    if (environment.IsDevelopment() && !string.IsNullOrWhiteSpace(fallbackDevelopmentValue))
    {
        return fallbackDevelopmentValue;
    }

    throw new InvalidOperationException($"Missing required configuration value '{key}'.");
}

static bool RequiresMachineCredential(PathString path)
{
    if (path.StartsWithSegments("/api/agent"))
    {
        return true;
    }

    var segments = path.Value?
        .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        ?? Array.Empty<string>();

    if (segments.Length < 4 || !string.Equals(segments[0], "api", StringComparison.OrdinalIgnoreCase))
    {
        return false;
    }

    if (!string.Equals(segments[1], "accounts", StringComparison.OrdinalIgnoreCase))
    {
        return false;
    }

    return (segments.Length == 4 && string.Equals(segments[3], "manual-required", StringComparison.OrdinalIgnoreCase))
        || (segments.Length == 5
            && string.Equals(segments[3], "proxies", StringComparison.OrdinalIgnoreCase)
            && string.Equals(segments[4], "rotate", StringComparison.OrdinalIgnoreCase));
}

static bool IsLoopbackOrigin(string origin)
{
    if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
    {
        return false;
    }

    return string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
        || string.Equals(uri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase);
}

static ClaimsPrincipal CreateMachinePrincipal()
{
    var identity = new ClaimsIdentity(
        new[]
        {
            new Claim(ClaimTypes.Name, "agent"),
            new Claim(ClaimTypes.Role, "Agent"),
            new Claim("role", "Agent")
        },
        authenticationType: "ApiKey");

    return new ClaimsPrincipal(identity);
}
