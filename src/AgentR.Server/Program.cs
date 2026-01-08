using AgentR.Server;
using Medallion.Threading;
using Medallion.Threading.Redis;
using StackExchange.Redis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Microsoft.AspNetCore.SignalR;
using AgentR.Shared;

var builder = WebApplication.CreateBuilder(args);

// 1. Connect to Redis
var redisConn = ConnectionMultiplexer.Connect("localhost:6379,abortConnect=false");

// 2. Register the Distributed Lock Provider
// This manages the "Talking Stick" across multiple servers
builder.Services.AddSingleton<IDistributedLockProvider>(_ => 
    new RedisDistributedSynchronizationProvider(redisConn.GetDatabase()));

// --- AUTHENTICATION (The Bouncer) ---
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    // For v1.0, we accept a "Fake" local token or valid Azure AD token
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

// 3. Configure SignalR with Redis Backplane
builder.Services.AddSignalR(options => 
{
    // Register the "HR Department"
    options.AddFilter<ContentSafetyFilter>();
})
.AddStackExchangeRedis("localhost:6379", options => {
    options.Configuration.ChannelPrefix = "AgentR";
})
.AddMessagePackProtocol(options =>
{
    options.SerializerOptions = MessagePack.MessagePackSerializerOptions.Standard
        .WithResolver(MessagePack.Resolvers.ContractlessStandardResolver.Instance);
});

var app = builder.Build();

app.UseAuthentication(); // <--- NEW: Must be before Authorization
app.UseAuthorization();  // <--- NEW

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

app.Run();

// Make Program accessible to integration tests
public partial class Program { }
