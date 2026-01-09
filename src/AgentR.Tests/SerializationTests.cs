using MessagePack;
using AgentR.Shared;
using Xunit;

namespace AgentR.Tests;

public class SerializationTests
{
    [Fact]
    public void AgentFrame_ShouldRoundtrip_ViaMessagePack()
    {
        // 1. Arrange - Polymorphic frame
        AgentFrame original = new MessageFrame 
        { 
            SenderId = "Agent-1", 
            RoomId = "Test-Room", 
            Content = "Signal Received." 
        };

        // 2. Act
        var bytes = MessagePackSerializer.Serialize<AgentFrame>(original);
        var deserialized = MessagePackSerializer.Deserialize<AgentFrame>(bytes);

        // 3. Assert
        Assert.NotNull(deserialized);
        Assert.IsType<MessageFrame>(deserialized);
        Assert.Equal("Agent-1", deserialized.SenderId);
        Assert.Equal("Signal Received.", ((MessageFrame)deserialized).Content);
    }

    [Fact]
    public void DiscoveryFrame_ShouldRoundtrip_NestedObjects()
    {
        // 1. Arrange
        var original = new DiscoveryFrame
        {
            SenderId = "Hub",
            RoomId = "Lobby",
            Rooms = new List<RoomInfo>
            {
                new RoomInfo { RoomId = "Room-A", AgentCount = 5, Tags = new[] { "tag1" } }
            }
        };

        // 2. Act
        var bytes = MessagePackSerializer.Serialize<AgentFrame>(original);
        var deserialized = MessagePackSerializer.Deserialize<AgentFrame>(bytes);

        // 3. Assert
        Assert.IsType<DiscoveryFrame>(deserialized);
        var disco = (DiscoveryFrame)deserialized;
        Assert.Single(disco.Rooms);
        Assert.Equal("Room-A", disco.Rooms[0].RoomId);
        Assert.Equal(5, disco.Rooms[0].AgentCount);
    }
}
