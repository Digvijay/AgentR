using AgentR.Server;
using StackExchange.Redis;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AgentR.Tests;

public class DiscoveryServiceTests : IClassFixture<AgentRFactory>
{
    private readonly AgentRFactory _factory;

    public DiscoveryServiceTests(AgentRFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task UpdateAgentCount_ShouldBeAtomic()
    {
        // Arrange
        var redis = _factory.Services.GetRequiredService<IConnectionMultiplexer>();
        var service = new RoomDiscoveryService(redis);
        var roomId = "AtomicRoom";
        
        // Ensure clean state
        var db = redis.GetDatabase();
        await db.KeyDeleteAsync($"agentr:room:{roomId}");
        await db.KeyDeleteAsync("agentr:rooms");

        await service.RegisterRoomAsync(roomId);

        // Act - Concurrent updates with 10 UNIQUE agents
        var tasks = Enumerable.Range(0, 10).Select(i => service.UpdateAgentCountAsync(roomId, $"Agent-{i}", 1));
        await Task.WhenAll(tasks);

        // Assert
        var rooms = await service.ListRoomsAsync();
        var room = rooms.FirstOrDefault(r => r.RoomId == roomId);
        Assert.NotNull(room);
        Assert.Equal(10, room.AgentCount);
    }

    [Fact]
    public async Task ListRooms_WithTags_ShouldFilterCorrectly()
    {
        // Arrange
        var redis = _factory.Services.GetRequiredService<IConnectionMultiplexer>();
        var service = new RoomDiscoveryService(redis);
        
        await service.RegisterRoomAsync("PythonRoom", new[] { "python", "fast" });
        await service.RegisterRoomAsync("CSharpRoom", new[] { "csharp", "fast" });
        await service.RegisterRoomAsync("GoRoom", new[] { "go", "slow" });

        // Act
        var fastRooms = await service.ListRoomsAsync(new[] { "fast" });
        var pythonRooms = await service.ListRoomsAsync(new[] { "python" });

        // Assert
        Assert.Equal(2, fastRooms.Count);
        Assert.Single(pythonRooms);
        Assert.Equal("PythonRoom", pythonRooms[0].RoomId);
    }
}
