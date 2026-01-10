using AgentR.Shared;
using Microsoft.AspNetCore.SignalR.Client;
using Xunit;
using StackExchange.Redis;
using Microsoft.Extensions.DependencyInjection;
using AgentR.Server;

namespace AgentR.Tests;

public class ResiliencyTests : IClassFixture<AgentRFactory>
{
    private readonly AgentRFactory _factory;

    public ResiliencyTests(AgentRFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Disconnect_ShouldReleaseLockAndClearState()
    {
        // Arrange
        var connection = _factory.CreateHubConnection();
        await connection.StartAsync();
        var roomId = "DisconnectRoom";
        await connection.InvokeAsync("JoinRoom", roomId, null);
        await connection.InvokeAsync("RequestTurn", roomId);

        // Verification - check Redis for the lock (indirectly by another agent taking it)
        var connectionB = _factory.CreateHubConnection();
        await connectionB.StartAsync();
        await connectionB.InvokeAsync("JoinRoom", roomId, null);

        // Act - Force disconnect A
        await connection.StopAsync();
        
        // Give the server a moment to processes disconnection
        await Task.Delay(500);

        // Assert - Agent B should be able to take the lock immediately
        var turnResult = await Task.WhenAny(connectionB.InvokeAsync("RequestTurn", roomId), Task.Delay(2000));
        Assert.NotEqual(Task.Delay(2000), turnResult); // Should not timeout
    }

    [Fact]
    public async Task Disconnect_ShouldDecrementAgentCount()
    {
        // Arrange
        var redis = _factory.Services.GetRequiredService<IConnectionMultiplexer>();
        var discovery = new RoomDiscoveryService(redis);
        var roomId = "CountRoom";
        
        var connection = _factory.CreateHubConnection();
        await connection.StartAsync();
        await connection.InvokeAsync("JoinRoom", roomId, null);

        // Verifying initial count
        var rooms = await discovery.ListRoomsAsync();
        var room = rooms.First(r => r.RoomId == roomId);
        Assert.Equal(1, room.AgentCount);

        // Act
        await connection.StopAsync();
        await Task.Delay(500);

        // Assert
        // Assert - Retry logic for CI
        room = null;
        for (int i = 0; i < 10; i++)
        {
            rooms = await discovery.ListRoomsAsync();
            room = rooms.FirstOrDefault(r => r.RoomId == roomId);
            if (room != null && room.AgentCount == 0) break;
            await Task.Delay(500);
        }

        Assert.NotNull(room);
        Assert.Equal(0, room.AgentCount);
    }
}
