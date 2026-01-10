using System;

namespace AgentR.Shared;

/// <summary>
/// A ticket representing a request for an agent to perform work in a room.
/// This is used by the Lobby pattern for event-driven scaling (KEDA).
/// </summary>
public record AgentTicket
{
    /// <summary>
    /// The unique identifier of the room where the work is needed.
    /// </summary>
    public string RoomId { get; init; } = string.Empty;

    /// <summary>
    /// The timestamp when the request was created.
    /// </summary>
    public DateTime RequestedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Optional metadata or context for the agent.
    /// </summary>
    public Dictionary<string, string>? Metadata { get; init; }
}
