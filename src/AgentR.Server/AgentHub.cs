using Medallion.Threading;
using Microsoft.AspNetCore.SignalR;
using AgentR.Shared;
using StackExchange.Redis;
using System.Collections.Concurrent;
using Microsoft.AspNetCore.Authorization;
using System.Text.Json;
using AgentR.Server.Gateways;
using AgentR.Server.Governance;
using System.Diagnostics.Metrics;

namespace AgentR.Server;

// Authorization is handled via middleware (UseAuthentication/UseAuthorization in Program.cs)
// This allows conditional auth based on AgentR:DisableAuth configuration
public class AgentHub : Hub<IAgentClient>
{
    private readonly IDistributedLockProvider _lockProvider;
    private readonly RoomDiscoveryService _discovery;
    private readonly IDatabase _redis;
    
    // --- OTel Metrics ---
    private static readonly Meter MyMeter = new("AgentR.Server", "1.0");
    private static readonly Counter<double> BudgetBurned = MyMeter.CreateCounter<double>("agentr.budget.burned", "USD", "Money spent on tokens");
    private static readonly Counter<long> LockContention = MyMeter.CreateCounter<long>("agentr.lock.contention", "failures", "Failed lock acquisition attempts");
    
    // We still track local connections to handle disconnects, 
    // but the AUTHORITY is now Redis.
    private static readonly ConcurrentDictionary<string, string> _localConnectionMap = new();

    private readonly McpGatewayService _mcpGateway;
    private readonly IBudgetService _budget;
    private readonly ILogger<AgentHub> _logger;
    private readonly IConfiguration _configuration;
    
    public AgentHub(
        IDistributedLockProvider lockProvider, 
        RoomDiscoveryService discovery, 
        IConnectionMultiplexer redis, 
        McpGatewayService mcpGateway, 
        IBudgetService budget,
        ILogger<AgentHub> logger,
        IConfiguration configuration)
    {
        _lockProvider = lockProvider;
        _discovery = discovery;
        _redis = redis.GetDatabase();
        _mcpGateway = mcpGateway;
        _budget = budget;
        _logger = logger;
        _configuration = configuration;
    }

    // New RPC Method for MCP Tools
    public async Task InvokeTool(ToolRequestFrame request)
    {
        try
        {
            // 2. Execute on Server Side (Secure Environment)
            var result = await _mcpGateway.ExecuteToolAsync(
                request.ServerName, 
                request.ToolName, 
                request.ArgumentsJson,
                Context.ConnectionAborted
            );

            // 3. BROADCAST the request to the room so the Dashboard can see the "Handshake"
            await Clients.Group(request.RoomId).ReceiveSignal(request);

            // 4. Send Response back to Specific Caller (Private)
            await Clients.Caller.ReceiveSignal(new ToolResponseFrame
            {
                SenderId = "GATEWAY",
                RoomId = request.RoomId,
                RequestId = request.RequestId,
                Success = true,
                ResultJson = result
            });
        }
        catch (Exception ex)
        {
            await Clients.Caller.ReceiveSignal(new ToolResponseFrame
            {
                SenderId = "GATEWAY",
                RoomId = request.RoomId,
                RequestId = request.RequestId,
                Success = false,
                ResultJson = JsonSerializer.Serialize(new { error = ex.Message })
            });
        }
    }


    // --- Demo / Visualization Methods ---

    public async Task<long> GetRoomBalance(string roomId)
    {
        return await _budget.GetBalanceAsync(roomId);
    }

    public async Task TopUpRoom(string roomId, long amount)
    {
        await _budget.TopUpAsync(roomId, amount);
        
        // Notify the room of the top-up
        await Clients.Group(roomId).ReceiveSignal(new MessageFrame
        {
            SenderId = "FINOPS",
            RoomId = roomId,
            Content = $"💰 RECHARGE: Room topped up by {amount} credits."
        });
    }

    public Task SetBudgetStatus(bool enabled)
    {
        _budget.IsEnforcementEnabled = enabled;
        return Clients.All.ReceiveSignal(new MessageFrame 
        { 
            SenderId = "FINOPS", 
            RoomId = "SYSTEM", 
            Content = enabled ? "🔒 Budget Enforcement ENABLED" : "🔓 Budget Enforcement DISABLED" 
        });
    }

    public Task<bool> GetBudgetStatus()
    {
        return Task.FromResult(_budget.IsEnforcementEnabled);
    }

    public async Task JoinRoom(string roomId, string[]? tags = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(roomId);

        // 1. Avoid redundant joins for the same room
        if (_localConnectionMap.TryGetValue(Context.ConnectionId, out var existingRoomId))
        {
            if (existingRoomId == roomId)
            {
                // Already in this room, just refresh registration/tags
                await _discovery.RegisterRoomAsync(roomId, tags, Context.ConnectionAborted);
                return;
            }

            // Switching rooms: Clean up old room state first
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, existingRoomId);
            await _discovery.UpdateAgentCountAsync(existingRoomId, GetAgentId(), -1, Context.ConnectionAborted);
            _logger.LogInformation("Connection {ConnectionId} left {OldRoom} to join {NewRoom}", Context.ConnectionId, existingRoomId, roomId);
        }

        // 2. Join the new room
        await Groups.AddToGroupAsync(Context.ConnectionId, roomId, Context.ConnectionAborted);
        await _discovery.RegisterRoomAsync(roomId, tags, Context.ConnectionAborted);
        await _discovery.UpdateAgentCountAsync(roomId, GetAgentId(), 1, Context.ConnectionAborted);
        
        _localConnectionMap[Context.ConnectionId] = roomId;
        _logger.LogInformation("Connection {ConnectionId} joined room {RoomId}", Context.ConnectionId, roomId);
    }

    public async Task Broadcast(AgentFrame frame)
    {
        // SECURITY: Only the lock holder can broadcast
        // We verify if THIS connection holds the distributed lock handle.
        if (Context.Items.TryGetValue("TurnLock", out _))
        {
            // OTel: Track Money Burn
            if (frame is ThinkingFrame)
            {
                BudgetBurned.Add(0.001, 
                    new KeyValuePair<string, object?>("room_id", frame.RoomId),
                    new KeyValuePair<string, object?>("agent_id", frame.SenderId)
                );
            }
        
            // Send to everyone else
            await Clients.GroupExcept(frame.RoomId, Context.ConnectionId)
                         .ReceiveSignal(frame);
        }
    }

    public async Task RequestTurn(string roomId)
    {
        // 1. Try to acquire the Redis Lock (Non-blocking)
        // Key: "lock:room:DemoRoom"
        var lockKey = $"lock:room:{roomId}";
        
        // We ask for a lock with 0 timeout. If taken, fail immediately.
        var handle = await _lockProvider.TryAcquireLockAsync(lockKey, TimeSpan.Zero, Context.ConnectionAborted);

        if (handle != null)
        {
            // SUCCESS: We own the room now.
            
            // Store the lock handle in the Connection Context so we can release it later
            Context.Items["TurnLock"] = handle;

            // --- LEASE TIMER (Zombie Protection) ---
            var leaseTimeoutSeconds = _configuration.GetValue<int>("AgentR:LeaseTimeoutSeconds", 60);
            if (leaseTimeoutSeconds > 0)
            {
                var leaseCts = new CancellationTokenSource();
                Context.Items["LeaseCts"] = leaseCts;
                _ = WatchDogAsync(handle, roomId, Context.ConnectionId, leaseCts.Token, TimeSpan.FromSeconds(leaseTimeoutSeconds));
            }

            _localConnectionMap[Context.ConnectionId] = roomId;

            _logger.LogInformation("Distributed Lock acquired for {RoomId} by {ConnectionId}", roomId, Context.ConnectionId);

            // CAST TO BASE TYPE to force Union Serialization
            AgentFrame signal = new TurnGrantedFrame 
            { 
                SenderId = "HOST", 
                RoomId = roomId 
            };
            await Clients.Caller.ReceiveSignal(signal);
        }
        else
        {
            // OTel: Record Contention
            LockContention.Add(1, new KeyValuePair<string, object?>("room_id", roomId));
        
            // FAIL: Someone else holds the lock (maybe on another server)
            AgentFrame signal = new MessageFrame 
            { 
                SenderId = "HOST", 
                RoomId = roomId, 
                Content = "BUSY: Room is locked by another agent." 
            };
            await Clients.Caller.ReceiveSignal(signal);
        }
    }

    private async Task WatchDogAsync(IDistributedSynchronizationHandle handle, string roomId, string connectionId, CancellationToken ct, TimeSpan timeout)
    {
        try
        {
            await Task.Delay(timeout, ct);
            
            // If we get here, the token was NOT cancelled, meaning the lease expired.
            _logger.LogWarning("⏰ Lease EXPIRED for {RoomId} (Connection {ConnectionId}). Forcing release.", roomId, connectionId);
            
            await handle.DisposeAsync();

            // Notify the zombie agent (if still connected)
            await Clients.Client(connectionId).ReceiveSignal(new InterruptFrame 
            { 
                SenderId = "HOST", 
                RoomId = roomId, 
                Reason = "Lease Timeout (60s)"
            });
        }
        catch (TaskCanceledException)
        {
            // Normal: Turn released before timeout
        }
    }

    public async Task ReleaseTurn(string roomId)
    {
        await ReleaseTurnInternal();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        // Release lock if held
        await ReleaseTurnInternal();

        if (_localConnectionMap.TryRemove(Context.ConnectionId, out var roomId))
        {
            await _discovery.UpdateAgentCountAsync(roomId, GetAgentId(), -1);
        }
        
        await base.OnDisconnectedAsync(exception);
    }

    public async Task LeaveRoom(string roomId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, roomId);
        if (_localConnectionMap.TryRemove(Context.ConnectionId, out _))
        {
            await _discovery.UpdateAgentCountAsync(roomId, GetAgentId(), -1, Context.ConnectionAborted);
        }
    }

    private string GetAgentId() => Context.UserIdentifier ?? Context.ConnectionId;

    private async Task ReleaseTurnInternal()
    {
        if (Context.Items.TryGetValue("LeaseCts", out var ctsObj) && ctsObj is CancellationTokenSource cts)
        {
            cts.Cancel();
            cts.Dispose();
            Context.Items.Remove("LeaseCts");
        }

        if (Context.Items.TryGetValue("TurnLock", out var lockObj) && 
            lockObj is IDistributedSynchronizationHandle handle)
        {
            await handle.DisposeAsync();
            Context.Items.Remove("TurnLock");
            _logger.LogInformation("Lock released for {ConnectionId}", Context.ConnectionId);

            // BROADCAST: Signal the room that the floor is now open
            if (_localConnectionMap.TryGetValue(Context.ConnectionId, out var roomId))
            {
                await Clients.Group(roomId).ReceiveSignal(new FloorOpenFrame 
                { 
                    SenderId = "HOST", 
                    RoomId = roomId 
                });
            }
        }
    }

    public async Task AdminInterrupt(string roomId)
    {
        // This is a "Global Shout" - SignalR Backplane ensures all Servers send this
        AgentFrame signal = new InterruptFrame 
        { 
            SenderId = "ADMIN", 
            RoomId = roomId, 
            Reason = "Admin Override" 
        };
        await Clients.Group(roomId).ReceiveSignal(signal);
    }

    public async Task<List<RoomInfo>> ListRooms(string[]? tags)
    {
        return await _discovery.ListRoomsAsync(tags);
    }

    public async Task AssignAgent(string targetAgentId, string newRoomId)
    {
        // Pushes an AssignmentFrame to a specific Agent (System-wide via sub claim)
        AgentFrame signal = new AssignmentFrame
        {
            SenderId = "HOST",
            RoomId = "SYSTEM", 
            NewRoomId = newRoomId
        };
        await Clients.User(targetAgentId).ReceiveSignal(signal);
    }

    public async Task RequestAgent(string agentType)
    {
        if (!_localConnectionMap.TryGetValue(Context.ConnectionId, out var roomId))
        {
            throw new HubException("You must be in a room to request an agent.");
        }

        // 1. Create a "Ticket"
        var ticket = new AgentTicket 
        { 
            RoomId = roomId, 
            RequestedAt = DateTime.UtcNow 
        };

        // 2. Push to Redis List (The KEDA Trigger)
        // Key: "agentr:lobby:python"
        var queueKey = $"agentr:lobby:{agentType.ToLower()}";
        
        await _redis.ListRightPushAsync(queueKey, JsonSerializer.Serialize(ticket));

        // 3. Notify the room that help is coming
        AgentFrame signal = new MessageFrame 
        { 
            SenderId = "SYSTEM", 
            RoomId = roomId, 
            Content = $"⏳ Ticket queued for {agentType}. Waking up agents..." 
        };
        await Clients.Group(roomId).ReceiveSignal(signal);
    }
}
