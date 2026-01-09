using StackExchange.Redis;
using System.Text.Json;
using AgentR.Shared;
using Xunit;

namespace AgentR.Tests;

public class LobbyTests
{
    private const string RedisConnectionString = "localhost:6379,abortConnect=false";

    [Fact]
    public async Task Lobby_ShouldCorrectlyRoundtripTickets()
    {
        // 1. Connect to Redis (Localhost assumed for integration tests)
        var redis = ConnectionMultiplexer.Connect(RedisConnectionString);
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
