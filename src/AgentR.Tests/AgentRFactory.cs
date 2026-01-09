using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using Testcontainers.Redis;
using Medallion.Threading.Redis;
using Medallion.Threading;
using AgentR.Server;
using AgentR.Server.Auditing;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using AgentR.Server.Governance;
using AgentR.Server.Gateways;
using AgentR.Client;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Encodings.Web;

namespace AgentR.Tests;

public class AgentRFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly RedisContainer _redisContainer = new RedisBuilder()
        .WithImage("redis:latest")
        .Build();

    public async Task InitializeAsync()
    {
        await _redisContainer.StartAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:redis", _redisContainer.GetConnectionString());

        builder.ConfigureLogging(logging => 
        {
            logging.ClearProviders();
            logging.AddConsole();
            logging.SetMinimumLevel(LogLevel.Debug);
        });

        builder.ConfigureTestServices(services =>
        {
            var redisConn = ConnectionMultiplexer.Connect(_redisContainer.GetConnectionString());
            services.AddSingleton<IConnectionMultiplexer>(redisConn);

            // Disable the SignalR Redis backplane for integration tests.
            // We replace it with the default in-memory manager.
            services.AddSingleton(typeof(HubLifetimeManager<>), typeof(DefaultHubLifetimeManager<>));

            services.AddSingleton<IDistributedLockProvider>(
                new RedisDistributedSynchronizationProvider(redisConn.GetDatabase()));

            services.AddSingleton<RoomDiscoveryService>();
            
            services.AddSingleton<AuditBuffer>();
            services.AddSingleton<IAuditStorage, LocalAuditStorage>(sp => 
                new LocalAuditStorage("test_flight_data"));

            services.AddSingleton<IBudgetService, RedisBudgetService>();
            services.AddSingleton<McpGatewayService>();
            services.AddSingleton<AuditFilter>();
            services.AddSingleton<ContentSafetyFilter>();
            services.AddSingleton<BudgetFilter>();

            // 1. Register Mock Authentication
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = "Test";
                options.DefaultChallengeScheme = "Test";
            })
                .AddScheme<AuthenticationSchemeOptions, MockAuthHandler>("Test", null);

            // 2. Bypass authorization for original tests
            services.AddAuthorization(options =>
            {
                options.DefaultPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
                    .RequireAssertion(_ => true) 
                    .Build();
            });
        });
    }

    public new async Task DisposeAsync()
    {
        await _redisContainer.DisposeAsync();
    }

    // A fake authentication handler that always produces a "Tester" user
    public class MockAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public MockAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger, UrlEncoder encoder)
            : base(options, logger, encoder) { }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var claims = new[] { new Claim(ClaimTypes.Name, "Tester"), new Claim(ClaimTypes.NameIdentifier, "TesterId") };
            var identity = new ClaimsIdentity(claims, "Test");
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, "Test");

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }

    public HubConnection CreateHubConnection() => CreateHubConnection(Server);

    public HubConnection CreateHubConnection(TestServer server)
    {
        var handler = server.CreateHandler();
        var baseAddress = server.BaseAddress;

        return new HubConnectionBuilder()
            .WithUrl(new Uri(baseAddress, "agentHub"), o =>
            {
                o.HttpMessageHandlerFactory = _ => handler;
            })
            .AddJsonProtocol()
            .Build();
    }

    public AgentClient CreateAgentClient(string agentId, string apiKey)
    {
        var hubUrl = new Uri(Server.BaseAddress, "agentHub").ToString();
        var handler = Server.CreateHandler();
        return new AgentClient(hubUrl, agentId, apiKey, handler);
    }
}
