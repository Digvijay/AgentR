using AgentR.Shared;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Xunit;

namespace AgentR.Tests;

public class SafetyTests : IClassFixture<AgentRFactory>
{
    private readonly AgentRFactory _factory;

    public SafetyTests(AgentRFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ContentSafetyFilter_ShouldBlockBannedWords_InMessageFrame()
    {
        // Arrange
        var connection = _factory.CreateHubConnection();
        await connection.StartAsync();
        var roomId = "SafetyRoom1";
        await connection.InvokeAsync("JoinRoom", roomId, null);

        // Act & Assert
        // "mutiny" is a banned word
        var frame = new MessageFrame 
        { 
            RoomId = roomId, 
            SenderId = "PirateAgent", 
            Content = "Let's start a mutiny!" 
        };

        var exception = await Assert.ThrowsAsync<HubException>(() => 
            connection.InvokeAsync("Broadcast", frame));

        Assert.Contains("Safety Policy Violation", exception.Message);
    }

    [Fact]
    public async Task ContentSafetyFilter_ShouldBlockBannedWords_InThinkingFrame()
    {
        // Arrange
        var connection = _factory.CreateHubConnection();
        await connection.StartAsync();
        var roomId = "SafetyRoom2";
        await connection.InvokeAsync("JoinRoom", roomId, null);

        // Act & Assert
        // "kill" is a banned word
        var frame = new ThinkingFrame 
        { 
            RoomId = roomId, 
            SenderId = "AggressiveAgent", 
            Content = "I want to kill the process." 
        };

        var exception = await Assert.ThrowsAsync<HubException>(() => 
            connection.InvokeAsync("Broadcast", frame));

        Assert.Contains("Safety Policy Violation", exception.Message);
    }

    [Fact]
    public async Task ContentSafetyFilter_ShouldAllowSafeContent()
    {
        // Arrange
        var connection = _factory.CreateHubConnection();
        await connection.StartAsync();
        var roomId = "SafetyRoom3";
        await connection.InvokeAsync("JoinRoom", roomId, null);

        // Act
        var frame = new MessageFrame 
        { 
            RoomId = roomId, 
            SenderId = "GoodAgent", 
            Content = "Everything is fine." 
        };

        // No exception should be thrown
        await connection.InvokeAsync("Broadcast", frame);
    }
}
