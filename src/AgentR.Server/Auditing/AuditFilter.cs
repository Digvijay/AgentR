using Microsoft.AspNetCore.SignalR;
using AgentR.Shared;
using Microsoft.Extensions.Logging;

namespace AgentR.Server.Auditing;

/// <summary>
/// A SignalR <see cref="IHubFilter"/> that intercepts agent communications and records them 
/// via the <see cref="AuditBuffer"/>. This forms the core of the "Network-Level Auditing" feature.
/// </summary>
public class AuditFilter : IHubFilter
{
    private readonly AuditBuffer _buffer;
    private readonly ILogger<AuditFilter> _logger;

    public AuditFilter(AuditBuffer buffer, ILogger<AuditFilter> logger)
    {
        _buffer = buffer;
        _logger = logger;
    }

    public async ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext context, Func<HubInvocationContext, ValueTask<object?>> next)
    {
        // 1. Capture "Broadcast" events (Thinking, Messages)
        if (context.HubMethodName == "Broadcast" && 
            context.HubMethodArguments[0] is AgentFrame frame)
        {
            var content = frame switch 
            {
                ThinkingFrame t => t.Content,
                MessageFrame m => m.Content,
                InterruptFrame i => $"[INTERRUPT] {i.Reason}",
                DiscoveryFrame d => $"[DISCOVERY] Rooms: {d.Rooms.Count}",
                AssignmentFrame a => $"[ASSIGNMENT] To: {a.NewRoomId}",
                _ => "{Signal Only}"
            };

            _buffer.Record(new AuditRecord
            {
                RoomId = frame.RoomId,
                AgentId = frame.SenderId,
                ConnectionId = context.Context.ConnectionId,
                EventType = frame.GetType().Name,
                Content = content
            });
        }
        
        // 2. Capture "RequestTurn" events (Logic Flow)
        if (context.HubMethodName == "RequestTurn")
        {
             _buffer.Record(new AuditRecord
            {
                RoomId = context.HubMethodArguments[0]?.ToString() ?? "Unknown",
                AgentId = "Server", // Captured *before* we know if they got the lock
                EventType = "TurnRequested",
                Content = "Agent requested microphone."
            });
        }

        // Execute the actual method
        return await next(context);
    }
}
