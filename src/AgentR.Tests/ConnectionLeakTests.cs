using AgentR.Shared;
using Microsoft.AspNetCore.SignalR.Client;
using Xunit;
using Microsoft.Extensions.DependencyInjection;
using AgentR.Server;
using StackExchange.Redis;

namespace AgentR.Tests;

public class ConnectionLeakTests : IClassFixture<AgentRFactory>
{
    private readonly AgentRFactory _factory;

    public ConnectionLeakTests(AgentRFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task JoinRoom_ShouldBeIdempotent_AndNotLeakOnSwitch()
    {
        // Arrange
        var connection = _factory.CreateHubConnection();
        await connection.StartAsync();
        
        var redis = _factory.Services.GetRequiredService<IConnectionMultiplexer>();
        var discovery = new RoomDiscoveryService(redis);

        // Act 1: Join same room 5 times
        for (int i = 0; i < 5; i++)
        {
            await connection.InvokeAsync("JoinRoom", "SpamRoom", null);
        }

        // Assert 1: Count should be 1, not 5
        var rooms = await discovery.ListRoomsAsync();
        var spamRoom = rooms.FirstOrDefault(r => r.RoomId == "SpamRoom");
        Assert.NotNull(spamRoom);
        Assert.Equal(1, spamRoom.AgentCount);

        // Act 2: Switch to another room
        await connection.InvokeAsync("JoinRoom", "NewRoom", null);

        // Assert 2: SpamRoom count should be 0, NewRoom should be 1
        rooms = await discovery.ListRoomsAsync();
        spamRoom = rooms.FirstOrDefault(r => r.RoomId == "SpamRoom");
        var newRoom = rooms.FirstOrDefault(r => r.RoomId == "NewRoom");
        
        Assert.Equal(0, spamRoom?.AgentCount ?? 0);
        Assert.Equal(1, newRoom?.AgentCount ?? 0);
    }

    [Fact]
    public async Task MultipleConnections_SameAgent_ShouldCountAsOne()
    {
        // Arrange
        
        // We use AgentRClient which uses the MockAuth we set up
        // Note: Our MockAuth currently returns "TesterId". 
        // I'll use the CreateHubConnection with a header if I want to simulate different IDs,
        // but for now let's just use the factory's CreateAgentClient which supports Auth.
        // Wait, the MockAuthHandler is hardcoded to "TesterId".
        
        var conn1 = _factory.CreateHubConnection(); 
        var conn2 = _factory.CreateHubConnection();

        await conn1.StartAsync();
        await conn2.StartAsync();

        var redis = _factory.Services.GetRequiredService<IConnectionMultiplexer>();
        var discovery = new RoomDiscoveryService(redis);

        // Act
        // Since both use MockAuth, they both have Context.UserIdentifier = "TesterId"
        await conn1.InvokeAsync("JoinRoom", "UniqueRoom", null);
        await conn2.InvokeAsync("JoinRoom", "UniqueRoom", null);

        // Assert
        var rooms = await discovery.ListRoomsAsync();
        var room = rooms.FirstOrDefault(r => r.RoomId == "UniqueRoom");
        Assert.NotNull(room);
        Assert.Equal(1, room.AgentCount); // Two connections, ONE agent ID

        // Act 2: Disconnect one
        await conn1.StopAsync();
        await Task.Delay(500);

        // Assert 2: Agent count should STILL be 1 because conn2 is alive
        rooms = await discovery.ListRoomsAsync();
        room = rooms.FirstOrDefault(r => r.RoomId == "UniqueRoom");
        Assert.Equal(1, room?.AgentCount);

        // Act 3: Disconnect both
        await conn2.StopAsync();
        await Task.Delay(500);

        // Assert 3: Agent count should be 0
        rooms = await discovery.ListRoomsAsync();
        room = rooms.FirstOrDefault(r => r.RoomId == "UniqueRoom");
        Assert.Equal(0, room?.AgentCount ?? 0);
    }
}
