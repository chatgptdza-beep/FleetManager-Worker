using FleetManager.Application.Abstractions;
using FleetManager.Contracts.Agent;
using FleetManager.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.SignalR;
using Microsoft.IdentityModel.Tokens;
using System.Text;
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSignalR();

// Configure JWT Authentication
var jwtSettings = builder.Configuration.GetSection("Jwt");
var secretKey = jwtSettings["Key"] ?? "12345678901234567890123456789012"; // 32 chars minimum
var encodedKey = Encoding.UTF8.GetBytes(secretKey);

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = false;
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(encodedKey),
        ValidateIssuer = false,
        ValidateAudience = false,
        ValidateLifetime = true
    };
});

builder.Services.AddAuthorization();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials()
        .SetIsOriginAllowed(_ => true));
});

var app = builder.Build();

await app.Services.SeedDemoDataAsync();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
app.UseHttpsRedirection();

app.UseAuthentication();

// Simple API Key Middleware for Agents
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api/agent"))
    {
        var expectedApiKey = builder.Configuration["AgentApiKey"]
            ?? builder.Configuration["Agent:ApiKey"]
            ?? "MASTER-KEY-12345";
        if (!context.Request.Headers.TryGetValue("X-Api-Key", out var extractedApiKey) || extractedApiKey != expectedApiKey)
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsync("Unauthorized agent request");
            return;
        }
    }
    await next(context);
});

app.UseAuthorization();

// Operator login — returns a 24-hour JWT the Desktop uses as Bearer token
app.MapPost("/api/auth/token", (AuthTokenRequest req, IConfiguration config) =>
{
    var adminPassword = config["AdminPassword"] ?? "Admin@FleetMgr2026!";
    if (req.Password != adminPassword)
        return Results.Unauthorized();

    var key = config["Jwt:Key"] ?? "12345678901234567890123456789012";
    var keyBytes = Encoding.UTF8.GetBytes(key);
    var tokenDescriptor = new Microsoft.IdentityModel.Tokens.SecurityTokenDescriptor
    {
        Subject = new System.Security.Claims.ClaimsIdentity(new[]
        {
            new System.Security.Claims.Claim("name", "admin"),
            new System.Security.Claims.Claim("role", "Operator")
        }),
        Expires = DateTime.UtcNow.AddHours(24),
        SigningCredentials = new Microsoft.IdentityModel.Tokens.SigningCredentials(
            new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(keyBytes),
            Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256Signature)
    };
    var tokenHandler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
    var token = tokenHandler.CreateToken(tokenDescriptor);
    return Results.Ok(new { token = tokenHandler.WriteToken(token) });
}).AllowAnonymous();

app.MapControllers();
app.MapHub<FleetManager.Api.Hubs.OperationsHub>("/hubs/operations");

app.MapPost("/api/agent/heartbeat", async (
    AgentHeartbeatRequest request,
    INodeService nodeService,
    IHubContext<FleetManager.Api.Hubs.OperationsHub, FleetManager.Api.Hubs.IOperationsClient> hub,
    CancellationToken cancellationToken) =>
{
    await nodeService.UpdateHeartbeatAsync(
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

    // Broadcast real-time heartbeat to all connected Desktop clients
    await hub.Clients.All.SendNodeHeartbeatEvent(
        request.NodeId,
        request.CpuPercent,
        request.RamPercent,
        request.DiskPercent,
        request.ActiveSessions,
        request.PingMs);

    return Results.Accepted();
});

app.MapGet("/api/agent/nodes/{nodeId:guid}/commands/next", async (Guid nodeId, INodeService nodeService, CancellationToken cancellationToken) =>
{
    var command = await nodeService.GetNextPendingCommandAsync(nodeId, cancellationToken);
    return command is null ? Results.NoContent() : Results.Ok(command);
});

app.MapPost("/api/agent/commands/{commandId:guid}/complete", async (Guid commandId, AgentCommandCompletionRequest request, INodeService nodeService, CancellationToken cancellationToken) =>
{
    await nodeService.CompleteCommandAsync(request.NodeId, commandId, request.Succeeded, request.ResultMessage, cancellationToken);
    return Results.Accepted();
});

app.Run();

record AuthTokenRequest(string Password);
