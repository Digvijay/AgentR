using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using Testcontainers.Redis;
using Medallion.Threading.Redis;
using Medallion.Threading;
using AgentR.Server;

namespace AgentR.Tests;

public class AgentRFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    // 1. Define the Container
    private readonly RedisContainer _redisContainer = new RedisBuilder()
        .WithImage("redis:latest")
        .Build();

    // 2. Start Redis before tests
    public async Task InitializeAsync()
    {
        await _redisContainer.StartAsync();
    }

    // 3. Configure the App to use the Docker Redis
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            // Remove the "Real" Redis registration from Program.cs
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(IConnectionMultiplexer));
            if (descriptor != null) services.Remove(descriptor);

            // Add our "Test" Redis connection string
            var redisConn = ConnectionMultiplexer.Connect(_redisContainer.GetConnectionString());
            services.AddSingleton<IConnectionMultiplexer>(redisConn);

            // Re-register the Lock Provider with the Test Redis
            services.AddSingleton<IDistributedLockProvider>(
                new RedisDistributedSynchronizationProvider(redisConn.GetDatabase()));
                
            // Disable authentication for tests
            services.AddAuthorization(options =>
            {
                options.DefaultPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
                    .RequireAssertion(_ => true) // Always allow
                    .Build();
            });
        });
    }

    // 4. Cleanup
    public new async Task DisposeAsync()
    {
        await _redisContainer.DisposeAsync();
    }
}
