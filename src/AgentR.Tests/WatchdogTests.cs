using AgentR.Shared;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;
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
    public async Task ExpiredOwner_CannotBroadcast_AfterSuccessorAcquiresTurn()
    {
        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((context, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["AgentR:LeaseTimeoutSeconds"] = "2"
                });
            });
        });

        await using var formerOwner = _factory.CreateHubConnection(factory.Server);
        await using var successor = _factory.CreateHubConnection(factory.Server);
        await using var observer = _factory.CreateHubConnection(factory.Server);
        var expired = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var granted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var delivered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var messages = new ConcurrentQueue<string>();
        var roomId = $"ExpiredOwner-{Guid.NewGuid():N}";

        formerOwner.On<AgentFrame>("ReceiveSignal", frame =>
        {
            if (frame is InterruptFrame) expired.TrySetResult(true);
        });
        successor.On<AgentFrame>("ReceiveSignal", frame =>
        {
            if (frame is TurnGrantedFrame) granted.TrySetResult(true);
        });
        observer.On<AgentFrame>("ReceiveSignal", frame =>
        {
            if (frame is MessageFrame message)
            {
                messages.Enqueue(message.Content);
                if (message.Content == "current-owner") delivered.TrySetResult(true);
            }
        });

        await formerOwner.StartAsync();
        await successor.StartAsync();
        await observer.StartAsync();
        await formerOwner.InvokeAsync("JoinRoom", roomId, null);
        await successor.InvokeAsync("JoinRoom", roomId, null);
        await observer.InvokeAsync("JoinRoom", roomId, null);

        await formerOwner.InvokeAsync("RequestTurn", roomId);
        await expired.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await successor.InvokeAsync("RequestTurn", roomId);
        await granted.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var error = await Record.ExceptionAsync(() => formerOwner.InvokeAsync("Broadcast", new MessageFrame
        {
            SenderId = "former-owner",
            RoomId = roomId,
            Content = "stale-owner"
        }));
        await formerOwner.InvokeAsync("ReleaseTurn", roomId);
        await formerOwner.InvokeAsync("ReleaseTurn", roomId);
        await successor.InvokeAsync("Broadcast", new MessageFrame
        {
            SenderId = "successor",
            RoomId = roomId,
            Content = "current-owner"
        });
        await delivered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.DoesNotContain("stale-owner", messages);
        Assert.IsType<HubException>(error);
    }

    [Fact]
    public async Task ReleaseTurn_IsIdempotent_AndAllowsReacquisition()
    {
        await using var owner = _factory.CreateHubConnection();
        await using var observer = _factory.CreateHubConnection();
        var firstGrant = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondGrant = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var delivered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var grants = 0;
        var roomId = $"ReacquiredTurn-{Guid.NewGuid():N}";

        owner.On<AgentFrame>("ReceiveSignal", frame =>
        {
            if (frame is TurnGrantedFrame)
            {
                if (Interlocked.Increment(ref grants) == 1) firstGrant.TrySetResult(true);
                else secondGrant.TrySetResult(true);
            }
        });
        observer.On<AgentFrame>("ReceiveSignal", frame =>
        {
            if (frame is MessageFrame { Content: "reacquired-owner" }) delivered.TrySetResult(true);
        });

        await owner.StartAsync();
        await observer.StartAsync();
        await owner.InvokeAsync("JoinRoom", roomId, null);
        await observer.InvokeAsync("JoinRoom", roomId, null);
        await owner.InvokeAsync("RequestTurn", roomId);
        await firstGrant.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await owner.InvokeAsync("ReleaseTurn", roomId);
        await owner.InvokeAsync("ReleaseTurn", roomId);

        await Assert.ThrowsAsync<HubException>(() => owner.InvokeAsync("Broadcast", new MessageFrame
        {
            SenderId = "released-owner",
            RoomId = roomId,
            Content = "released-owner"
        }));

        await owner.InvokeAsync("RequestTurn", roomId);
        await secondGrant.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await owner.InvokeAsync("Broadcast", new MessageFrame
        {
            SenderId = "owner",
            RoomId = roomId,
            Content = "reacquired-owner"
        });
        await delivered.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task ActiveTurn_DoesNotAuthorizeAnotherRoom()
    {
        await using var owner = _factory.CreateHubConnection();
        var roomId = $"OwnedRoom-{Guid.NewGuid():N}";
        var otherRoomId = $"OtherRoom-{Guid.NewGuid():N}";
        var granted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        owner.On<AgentFrame>("ReceiveSignal", frame =>
        {
            if (frame is TurnGrantedFrame) granted.TrySetResult(true);
        });

        await owner.StartAsync();
        await owner.InvokeAsync("JoinRoom", roomId, null);
        await owner.InvokeAsync("RequestTurn", roomId);
        await granted.Task.WaitAsync(TimeSpan.FromSeconds(10));

        await Assert.ThrowsAsync<HubException>(() => owner.InvokeAsync("RequestTurn", otherRoomId));
        await Assert.ThrowsAsync<HubException>(() => owner.InvokeAsync("Broadcast", new MessageFrame
        {
            SenderId = "owner",
            RoomId = otherRoomId,
            Content = "wrong-room"
        }));
        await owner.InvokeAsync("Broadcast", new MessageFrame
        {
            SenderId = "owner",
            RoomId = roomId,
            Content = "still-owns-original-room"
        });
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
