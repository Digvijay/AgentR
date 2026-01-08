using MessagePack;
using System.Text.Json.Serialization;

namespace AgentR.Shared;

// 1. Define the Union (Map ID -> Type)
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(TurnGrantedFrame), "turn_granted")]
[JsonDerivedType(typeof(ThinkingFrame), "thinking")]
[JsonDerivedType(typeof(MessageFrame), "message")]
[JsonDerivedType(typeof(InterruptFrame), "interrupt")]
// MESSAGEPACK POLYMORPHISM SETUP:
[MessagePackObject] 
[Union(0, typeof(TurnGrantedFrame))]
[Union(1, typeof(ThinkingFrame))]
[Union(2, typeof(MessageFrame))]
[Union(3, typeof(InterruptFrame))]
public abstract record AgentFrame
{
    [Key(0)] // Explicit Index for MessagePack
    public required string SenderId { get; init; }
    
    [Key(1)]
    public required string RoomId { get; init; }
    
    [Key(2)]
    public long Timestamp { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

[MessagePackObject]
public record TurnGrantedFrame : AgentFrame
{
    // No extra fields, but acts as a unique type
}

[MessagePackObject]
public record ThinkingFrame : AgentFrame
{
    [Key(3)] // Continue the index count from Base (Base used 0,1,2)
    public required string Content { get; init; }
}

[MessagePackObject]
public record MessageFrame : AgentFrame
{
    [Key(3)]
    public required string Content { get; init; }
}

[MessagePackObject]
public record InterruptFrame : AgentFrame
{
    [Key(3)]
    public required string Reason { get; init; }
}

// SignalR Client Interface
public interface IAgentClient
{
    Task ReceiveSignal(AgentFrame frame);
}
