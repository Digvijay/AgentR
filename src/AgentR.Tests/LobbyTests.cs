using StackExchange.Redis;
using System.Text.Json;
using AgentR.Shared;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AgentR.Tests;

public class LobbyTests : IClassFixture<AgentRFactory>
{
    private readonly AgentRFactory _factory;

    public LobbyTests(AgentRFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Lobby_ShouldCorrectlyRoundtripTickets()
    {
        // 1. Connect to Redis via the factory's containerized instance
        var redis = _factory.Services.GetRequiredService<IConnectionMultiplexer>();
        var db = redis.GetDatabase();
        var queueKey = "agentr:lobby:test-agent";

        try
        {
            // Clean up
            await db.KeyDeleteAsync(queueKey);

            // 2. Create and Push
            var ticket = new AgentTicket 
            { 
                RoomId = "LobbyTestRoom", 
                RequestedAt = DateTime.UtcNow,
                Metadata = new Dictionary<string, string> { { "Priority", "High" } }
            };

            await db.ListRightPushAsync(queueKey, JsonSerializer.Serialize(ticket));

            // 3. Pop and Verify
            var result = await db.ListLeftPopAsync(queueKey);
            Assert.True(result.HasValue);

            var retrieved = JsonSerializer.Deserialize<AgentTicket>(result.ToString());
            Assert.NotNull(retrieved);
            Assert.Equal("LobbyTestRoom", retrieved.RoomId);
            Assert.Equal("High", retrieved.Metadata?["Priority"]);
        }
        finally
        {
            await db.KeyDeleteAsync(queueKey);
        }
    }
}
