using Medallion.Threading;
using Microsoft.AspNetCore.SignalR;
using AgentR.Shared;
using System.Collections.Concurrent;
using Microsoft.AspNetCore.Authorization;

namespace AgentR.Server;

[Authorize] // <--- THE GATE IS NOW CLOSED
public class AgentHub : Hub<IAgentClient>
{
    private readonly IDistributedLockProvider _lockProvider;
    
    // We still track local connections to handle disconnects, 
    // but the AUTHORITY is now Redis.
    private static readonly ConcurrentDictionary<string, string> _localConnectionMap = new();

    public AgentHub(IDistributedLockProvider lockProvider)
    {
        _lockProvider = lockProvider;
    }

    public async Task JoinRoom(string roomId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, roomId);
        Console.WriteLine($"✅ [{Context.ConnectionId}] Joined {roomId}");
    }

    public async Task Broadcast(AgentFrame frame)
    {
        // SECURITY: Only the lock holder can broadcast
        // We verify if THIS connection holds the distributed lock handle.
        if (Context.Items.TryGetValue("TurnLock", out _))
        {
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
        var handle = await _lockProvider.TryAcquireLockAsync(lockKey, TimeSpan.Zero);

        if (handle != null)
        {
            // SUCCESS: We own the room now.
            
            // Store the lock handle in the Connection Context so we can release it later
            Context.Items["TurnLock"] = handle;
            _localConnectionMap[Context.ConnectionId] = roomId;

            Console.WriteLine($"✅ Distributed Lock acquired for {roomId}");

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

    public async Task ReleaseTurn(string roomId)
    {
        if (Context.Items.TryGetValue("TurnLock", out var lockObj) && 
            lockObj is IDistributedSynchronizationHandle handle)
        {
            await handle.DisposeAsync(); // Releases Redis Lock
            Context.Items.Remove("TurnLock");
            _localConnectionMap.TryRemove(Context.ConnectionId, out _);
            
            Console.WriteLine($"🔓 Lock released for {roomId}");
        }
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        // ZOMBIE PROTECTION:
        // If the connection dies, the Context.Items are disposed,
        // and Medallion.Threading usually handles cleanup, but we force it here.
        if (Context.Items.TryGetValue("TurnLock", out var lockObj) && 
            lockObj is IDistributedSynchronizationHandle handle)
        {
            await handle.DisposeAsync();
            Console.WriteLine($"💀 Zombie Connection died. Lock auto-released.");
        }
        
        await base.OnDisconnectedAsync(exception);
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
        
        // Note: In v0.3 we would also forcefully break the Redis lock here
    }
}
