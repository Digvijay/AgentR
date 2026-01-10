using AgentR.Server;
using AgentR.Server.Auditing;

using Medallion.Threading;
using Medallion.Threading.Redis;
using StackExchange.Redis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Microsoft.AspNetCore.SignalR;
using AgentR.Shared;
using AgentR.Server.Governance;
using AgentR.Server.Gateways;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults(); // Enable OpenTelemetry, HealthChecks, etc.

builder.Services.AddOpenTelemetry()
   .WithMetrics(metrics => metrics.AddMeter("AgentR.Server")); // Listen for custom metrics


// 1. Connect to Redis (Using connection string from config for Aspire compatibility)
var redisConnectionString = builder.Configuration.GetConnectionString("redis") ?? "localhost:6379,abortConnect=false";
var redisConn = ConnectionMultiplexer.Connect(redisConnectionString);

// Register for DI
builder.Services.AddSingleton<IConnectionMultiplexer>(redisConn);

// 2. Register the Distributed Lock Provider
builder.Services.AddSingleton<IDistributedLockProvider>(_ => 
    new RedisDistributedSynchronizationProvider(redisConn.GetDatabase()));

// 2.1 Register Room Discovery Service
builder.Services.AddSingleton<RoomDiscoveryService>();

// 2.2 Register Auditing (Flight Recorder)
builder.Services.AddSingleton<AuditBuffer>();

if (!string.IsNullOrEmpty(builder.Configuration.GetConnectionString("AuditStorage")))
{
    builder.Services.AddSingleton<IAuditStorage, AzureBlobAuditStorage>();
}
else
{
    builder.Services.AddSingleton<IAuditStorage, LocalAuditStorage>();
}

builder.Services.AddHostedService<AuditService>();
builder.Services.AddSingleton<McpGatewayService>();
builder.Services.AddSingleton<AuditFilter>();
builder.Services.AddSingleton<ContentSafetyFilter>();

// 2.3 Register FinOps (Budget Controller)
builder.Services.AddSingleton<IBudgetService, RedisBudgetService>();
builder.Services.AddSingleton<BudgetFilter>();

// --- AUTHENTICATION (The Bouncer) ---
// For demo/dev: Set "AgentR:DisableAuth" = true in appsettings to skip authentication
var disableAuth = builder.Configuration.GetValue<bool>("AgentR:DisableAuth");

if (!disableAuth)
{
    builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        // For v1.0.0, we accept a "Fake" local token or valid Azure AD token
        // In production, you would point this to "https://login.microsoftonline.com/..."
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false, // Simplify for demo
            ValidateAudience = false, // Simplify for demo
            ValidateLifetime = false, // Simplify for demo
            ValidateIssuerSigningKey = true, 
            // In real life, use a real key/secret from config!
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("ThisIsMySuperSecretKeyForAgentR_MustBe32CharsLong!"))
        };

        // SIGNALR SPECIAL: Extract token from Query String (WebSockets can't do Headers)
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/agentHub"))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            }
        };
    });
}

// 3. Configure SignalR with Redis Backplane
builder.Services.AddSignalR(options => 
{
    // Register Governance & Safety Pipeline
    options.AddFilter<BudgetFilter>();        // 1. Check Money
    options.AddFilter<ContentSafetyFilter>(); // 2. Check Content
    options.AddFilter<AuditFilter>();         // 3. Log it (Flight Recorder)
})
.AddStackExchangeRedis(redisConnectionString, options => {
    options.Configuration.ChannelPrefix = StackExchange.Redis.RedisChannel.Literal("AgentR");
})
.AddJsonProtocol();

var app = builder.Build();

if (!disableAuth)
{
    app.UseAuthentication(); // <--- NEW: Must be before Authorization
    app.UseAuthorization();  // <--- NEW
}

app.MapDefaultEndpoints(); // Health checks, etc.

app.MapHub<AgentHub>("/agentHub");

// Keep the interrupt endpoint (it works across servers now!)
app.MapPost("/interrupt/{roomId}", async (string roomId, IHubContext<AgentHub, IAgentClient> hubContext) => 
{
    await hubContext.Clients.Group(roomId).ReceiveSignal(new InterruptFrame 
    { 
        SenderId = "ADMIN", 
        RoomId = roomId, 
        Reason = "Admin Override" 
    });
    return Results.Ok("Interrupted");
});

// Replay Endpoint: Basic implementation to read logs for a room
app.MapGet("/replay/{roomId}", async (string roomId, IAuditStorage storage, CancellationToken ct) => 
{
    var logs = await storage.GetRecordsAsync(roomId, ct);
    return Results.Ok(logs);
});

// FinOps Management: Top up a room's wallet
app.MapPost("/wallet/{roomId}/topup", async (string roomId, long amount, IBudgetService budget) => 
{
    await budget.TopUpAsync(roomId, amount);
    var newBalance = await budget.GetBalanceAsync(roomId);
    return Results.Ok(new { RoomId = roomId, Added = amount, NewBalance = newBalance });
});

app.Run();

// Make Program accessible to integration tests
public partial class Program { }
