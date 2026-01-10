using System;

namespace AgentR.Shared;

public record AuditRecord
{
    public string AuditId { get; init; } = Guid.NewGuid().ToString();
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    
    // Context
    public required string RoomId { get; init; }
    public required string AgentId { get; init; }
    public string? ConnectionId { get; init; }
    
    // The Payload (What happened)
    public string EventType { get; init; } = string.Empty; // "Thinking", "TurnGranted", "Interrupt"
    public string Content { get; init; } = string.Empty;   // The actual text/data
}
