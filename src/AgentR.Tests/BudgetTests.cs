using AgentR.Shared;
using AgentR.Server.Governance;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AgentR.Tests;

public class BudgetTests : IClassFixture<AgentRFactory>
{
    private readonly AgentRFactory _factory;

    public BudgetTests(AgentRFactory factory)
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
                o.HttpMessageHandlerFactory = _ => handler;
            })
            .AddJsonProtocol()
            .Build();
    }

    [Fact]
    public async Task Wallet_Starts_With_InitialGrant()
    {
        // Arrange
        var roomId = $"Room_{Guid.NewGuid()}";
        var scope = _factory.Services.CreateScope();
        var budgetService = scope.ServiceProvider.GetRequiredService<IBudgetService>();

        // Act - HasFundsAsync triggers the initial grant if key doesn't exist
        var hasFunds = await budgetService.HasFundsAsync(roomId);
        var balance = await budgetService.GetBalanceAsync(roomId);

        // Assert
        Assert.True(hasFunds);
        Assert.Equal(1000, balance); // InitialGrant value
    }

    [Fact]
    public async Task Hub_Rejects_Call_When_Budget_Exhausted()
    {
        // 1. Arrange - Seed a room with very low budget
        var roomId = $"BrokeRoom_{Guid.NewGuid()}";
        var client = _factory.CreateClient();
        
        // Use the new Top-up API to set a specific small balance
        // (HasFunds will give 1000, then we add 0, then we deduct manually)
        var scope = _factory.Services.CreateScope();
        var budgetService = scope.ServiceProvider.GetRequiredService<IBudgetService>();
        await budgetService.HasFundsAsync(roomId); // Trigger initial 1000
        await budgetService.DeductFundsAsync(roomId, 996); // Leave only 4 (less than 5 for a turn)

        var connection = CreateClient();
        await connection.StartAsync();

        // 2. Act & Assert
        // RequestTurn costs 50 credits. We invoke directly.
        var exception = await Assert.ThrowsAsync<HubException>(() => 
            connection.InvokeAsync("RequestTurn", roomId));
        
        Assert.Contains("402 Payment Required", exception.Message);
    }

    [Fact]
    public async Task TopUp_Allows_Operation_To_Resume()
    {
        // 1. Arrange - Create a room with 0 funds
        var roomId = $"RecoveryRoom_{Guid.NewGuid()}";
        var scope = _factory.Services.CreateScope();
        var budgetService = scope.ServiceProvider.GetRequiredService<IBudgetService>();
        
        await budgetService.HasFundsAsync(roomId); // Trigger 1000
        await budgetService.DeductFundsAsync(roomId, 1000); // Set to 0

        var connection = CreateClient();
        await connection.StartAsync();

        // Verify it fails initially
        await Assert.ThrowsAsync<HubException>(() => 
            connection.InvokeAsync("RequestTurn", roomId));

        // 2. Act - Admin Top-up (via API logic)
        await budgetService.TopUpAsync(roomId, 500);

        // 3. Assert - Now it should pass
        // This will succeed if it doesn't throw
        await connection.InvokeAsync("RequestTurn", roomId);
        
        var remaining = await budgetService.GetBalanceAsync(roomId);
        Assert.Equal(495, remaining); // 500 - 5 (turn cost)
    }

    [Fact]
    public async Task EmitMessage_Deducts_Funds()
    {
        // 1. Arrange
        var roomId = "ChargeRoom_" + Guid.NewGuid().ToString();
        var budgetService = _factory.Services.GetRequiredService<IBudgetService>();
        await budgetService.TopUpAsync(roomId, 100); // 100 credits

        var connection = CreateClient();
        await connection.StartAsync();

        // 2. Act
        // We use a raw invoke to verify the filter charges specifically for MessageFrame
        await connection.InvokeAsync("Broadcast", new MessageFrame 
        { 
            SenderId = "Tester", RoomId = roomId, Content = "Hello" 
        });

        // 3. Assert
        var balance = await budgetService.GetBalanceAsync(roomId);
        Assert.Equal(99, balance); // 100 - 1
    }
    [Fact]
    public async Task Hub_Deducts_Credits_For_Message_Frames()
    {
        // Arrange
        var roomId = "BudgetTestRoom_Msg";
        var connection = CreateClient();
        await connection.StartAsync();
        await connection.InvokeAsync("JoinRoom", roomId, null);

        // Act - Request turn (50) + Send Message (1)
        await connection.InvokeAsync("RequestTurn", roomId);
        await connection.InvokeAsync("Broadcast", new MessageFrame { RoomId = roomId, SenderId = "Tester", Content = "Hello" });

        // Assert
        var balance = await connection.InvokeAsync<long>("GetRoomBalance", roomId);
        // Initial 1000 - 5 - 1 = 994
        Assert.Equal(994, balance);
    }

    [Fact]
    public async Task Hub_Deducts_Credits_For_Thinking_Frames()
    {
        // Arrange
        var roomId = "BudgetTestRoom_Think";
        var connection = CreateClient();
        await connection.StartAsync();
        await connection.InvokeAsync("JoinRoom", roomId, null);

        // Act - Request turn (50) + Send 5 thinking frames (5x1)
        await connection.InvokeAsync("RequestTurn", roomId);
        for(int i=0; i<5; i++)
            await connection.InvokeAsync("Broadcast", new ThinkingFrame { RoomId = roomId, SenderId = "Tester", Content = "." });

        // Assert
        var balance = await connection.InvokeAsync<long>("GetRoomBalance", roomId);
        // 1000 - 5 - 5 = 990
        Assert.Equal(990, balance);
    }

    [Fact]
    public async Task Hub_Allows_Interrupts_Even_When_Broke()
    {
        // Arrange
        var roomId = "BudgetTestRoom_Broke";
        var scope = _factory.Services.CreateScope();
        var budgetService = scope.ServiceProvider.GetRequiredService<IBudgetService>();
        await budgetService.HasFundsAsync(roomId); // Init 1000
        await budgetService.DeductFundsAsync(roomId, 1000); // Now 0

        var connection = CreateClient();
        await connection.StartAsync();
        await connection.InvokeAsync("JoinRoom", roomId, null);

        // Act & Assert
        // RequestTurn (50) should fail
        await Assert.ThrowsAsync<HubException>(() => connection.InvokeAsync("RequestTurn", roomId));

        // Interrupt (Free) should SUCCEED
        await connection.InvokeAsync("Broadcast", new InterruptFrame { RoomId = roomId, SenderId = "Tester", Reason = "Emergency" });
        
        var balance = await connection.InvokeAsync<long>("GetRoomBalance", roomId);
        Assert.Equal(0, balance);
    }
}
