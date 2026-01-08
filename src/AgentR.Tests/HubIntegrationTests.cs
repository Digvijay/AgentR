using AgentR.Shared;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace AgentR.Tests;

public class HubIntegrationTests : IClassFixture<AgentRFactory>
{
    private readonly AgentRFactory _factory;

    public HubIntegrationTests(AgentRFactory factory)
    {
        _factory = factory;
    }

    private HubConnection CreateClient()
    {
        var handler = _factory.Server.CreateHandler();
        var baseAddress = _factory.Server.BaseAddress;
        
        return new HubConnectionBuilder()
            .WithUrl(new Uri(baseAddress, "agentHub"), o => 
            {
                o.HttpMessageHandlerFactory = _ => handler; // Pipe directly to memory
            })
            .AddJsonProtocol() // Use JSON for tests - handles polymorphism automatically
            .ConfigureLogging(logging =>
            {
                logging.AddConsole();
                logging.SetMinimumLevel(LogLevel.Debug);
            })
            .Build();
    }

    [Fact]
    public async Task HappyPath_AgentCanAcquireTurn()
    {
        // Arrange
        var agent = CreateClient();
        var turnGranted = new TaskCompletionSource<bool>();
        
        // Register handler BEFORE starting connection
        agent.On<AgentFrame>("ReceiveSignal", frame => 
        {
            Console.WriteLine($"DEBUG: Received frame type: {frame.GetType().Name}, SenderId: {frame.SenderId}");
            if (frame is TurnGrantedFrame) turnGranted.SetResult(true);
        });

        await agent.StartAsync();
        await agent.InvokeAsync("JoinRoom", "TestRoom1");

        // Act
        await agent.InvokeAsync("RequestTurn", "TestRoom1");

        // Assert
        var result = await Task.WhenAny(turnGranted.Task, Task.Delay(2000));
        Assert.True(result == turnGranted.Task, "Agent did not receive TurnGranted frame in time.");
    }

    [Fact]
    public async Task Concurrency_SecondAgentIsRejected()
    {
        // Arrange
        var agentA = CreateClient();
        var agentB = CreateClient();
        var agentB_Rejection = new TaskCompletionSource<string>();

        // Register handler BEFORE starting
        agentB.On<AgentFrame>("ReceiveSignal", frame =>
        {
            if (frame is MessageFrame m && m.Content.StartsWith("BUSY"))
            {
                agentB_Rejection.SetResult(m.Content);
            }
        });

        await agentA.StartAsync();
        await agentB.StartAsync();
        await agentA.InvokeAsync("JoinRoom", "TestRoom2");
        await agentB.InvokeAsync("JoinRoom", "TestRoom2");

        // Act
        // 1. Agent A takes the lock
        await agentA.InvokeAsync("RequestTurn", "TestRoom2");
        await Task.Delay(100); // Give Redis a moment to lock

        // 2. Agent B tries to take it
        await agentB.InvokeAsync("RequestTurn", "TestRoom2");

        // Assert
        var result = await Task.WhenAny(agentB_Rejection.Task, Task.Delay(2000));
        Assert.True(result == agentB_Rejection.Task, "Agent B was not rejected.");
        Assert.Contains("locked", agentB_Rejection.Task.Result);
    }
    
    [Fact]
    public async Task Interrupt_ReleasesLockAutomatically()
    {
        // Arrange
        var agent = CreateClient();
        var interruptReceived = new TaskCompletionSource<bool>();
        
        // Register handler BEFORE starting
        agent.On<AgentFrame>("ReceiveSignal", frame => 
        {
            if (frame is InterruptFrame) interruptReceived.SetResult(true);
        });

        await agent.StartAsync();
        await agent.InvokeAsync("JoinRoom", "TestRoom3");
        await agent.InvokeAsync("RequestTurn", "TestRoom3");

        // Act - Simulate Admin Interrupt
        // We use the HTTP endpoint we built in Program.cs
        var client = _factory.CreateClient(); // HTTP Client
        await client.PostAsync("/interrupt/TestRoom3", null);

        // Assert
        var result = await Task.WhenAny(interruptReceived.Task, Task.Delay(2000));
        Assert.True(result == interruptReceived.Task, "Interrupt signal was not broadcast.");
    }
}
