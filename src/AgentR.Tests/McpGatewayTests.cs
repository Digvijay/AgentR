using AgentR.Client;
using Xunit;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;

namespace AgentR.Tests;

public class McpGatewayTests : IClassFixture<AgentRFactory>
{
    private readonly AgentRFactory _factory;

    public McpGatewayTests(AgentRFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task InvokeTool_ShouldReturnError_ForUnknownServer()
    {
        // 1. Arrange
        var agentClient = _factory.CreateAgentClient("TestAgent", "test-key");
        await agentClient.ConnectAsync();

        // 2. Act & Assert
        var ex = await Assert.ThrowsAsync<Exception>(() => 
            agentClient.CallToolAsync("TestRoom", "non-existent", "any", new { }));
        
        Assert.Contains("non-existent", ex.Message);
    }
}
