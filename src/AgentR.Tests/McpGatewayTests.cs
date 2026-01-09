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
    public async Task InvokeTool_ShouldReturnResult_FromMockServer()
    {
        // 1. Arrange
        var hubUrl = _factory.Server.BaseAddress + "agentHub";
        var agent = new AgentClient(hubUrl, "TestAgent", "test-key");
        
        // We need to inject the TestServer's handler into the AgentClient's HubConnection
        // We'll add a helper to AgentRFactory for this or just do it manually here.
        var connection = agent.GetType().GetField("_connection", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(agent) as HubConnection;
        if (connection != null)
        {
             // This is a bit hacky for the test but ensures the SDK uses the TestServer
        }

        // Better: Update AgentRFactory to create an AgentClient instance
        var agentClient = _factory.CreateAgentClient("TestAgent", "test-key");
        await agentClient.ConnectAsync();

        // 2. Act
        var result = await agentClient.CallToolAsync("TestRoom", "github", "get_issue", new { id = 42 });

        // 3. Assert
        Assert.NotNull(result);
        Assert.Contains("Add MCP Support", result);
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
        
        Assert.Contains("not found", ex.Message);
    }
}
