using AgentR.Shared;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace AgentR.Tests;

public class WatchdogTests : IClassFixture<AgentRFactory>
{
    private readonly AgentRFactory _factory;

    public WatchdogTests(AgentRFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Watchdog_ShouldRevokeLock_AfterLeaseExceeds()
    {
        // Arrange
        // We create a custom factory with a very short lease timeout (2s)
        using var customFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((context, config) =>
            {
                config.AddInMemoryCollection(new[] 
                { 
                    new KeyValuePair<string, string?>("AgentR:LeaseTimeoutSeconds", "2") 
                });
            });
        });

        // Use the factory's server to create the connection
        var connection = _factory.CreateHubConnection(customFactory.Server);
        var interruptReceived = new TaskCompletionSource<string>();
        
        connection.On<AgentFrame>("ReceiveSignal", frame =>
        {
            if (frame is InterruptFrame i) interruptReceived.SetResult(i.Reason);
        });

        await connection.StartAsync();
        var roomId = "WatchdogRoom";
        await connection.InvokeAsync("JoinRoom", roomId, null);

        // Act
        // 1. Take the lock
        await connection.InvokeAsync("RequestTurn", roomId);

        // 2. Wait for expiration (Lease is 2s, give it 3s to be safe)
        var result = await Task.WhenAny(interruptReceived.Task, Task.Delay(5000));

        // Assert
        Assert.True(result == interruptReceived.Task, "Lease did not expire or InterruptFrame was not sent.");
        Assert.Contains("Lease Timeout", await interruptReceived.Task);

        // 3. Verify floor is open (Another agent can take it)
        var connectionB = _factory.CreateHubConnection(customFactory.Server);
        await connectionB.StartAsync();
        await connectionB.InvokeAsync("JoinRoom", roomId, null);
        
        var turnGrantedB = new TaskCompletionSource<bool>();
        connectionB.On<AgentFrame>("ReceiveSignal", f => { if (f is TurnGrantedFrame) turnGrantedB.SetResult(true); });
        
        await connectionB.InvokeAsync("RequestTurn", roomId);
        var turnResult = await Task.WhenAny(turnGrantedB.Task, Task.Delay(2000));
        Assert.True(turnResult == turnGrantedB.Task, "Lock was not successfully revoked/reclaimed.");
    }
}
