using MessagePack;
using System.Text.Json.Serialization;

namespace AgentR.Shared;

// 1. Define the Union (Map ID -> Type)
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(TurnGrantedFrame), "turn_granted")]
[JsonDerivedType(typeof(ThinkingFrame), "thinking")]
[JsonDerivedType(typeof(MessageFrame), "message")]
[JsonDerivedType(typeof(InterruptFrame), "interrupt")]
[JsonDerivedType(typeof(DiscoveryFrame), "discovery")]
[JsonDerivedType(typeof(ToolRequestFrame), "tool_request")]
[JsonDerivedType(typeof(ToolResponseFrame), "tool_response")]
[Union(0, typeof(TurnGrantedFrame))]
[Union(1, typeof(ThinkingFrame))]
[Union(2, typeof(MessageFrame))]
[Union(3, typeof(InterruptFrame))]
[Union(4, typeof(AssignmentFrame))]
[Union(5, typeof(DiscoveryFrame))]
[Union(6, typeof(ToolRequestFrame))]
[Union(7, typeof(ToolResponseFrame))]
public abstract record AgentFrame
{
    public required string SenderId { get; init; }
    public required string RoomId { get; init; }
    public long Timestamp { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

[MessagePackObject(keyAsPropertyName: true)]
public record TurnGrantedFrame : AgentFrame
{
    // No extra fields
}

[MessagePackObject(keyAsPropertyName: true)]
public record ThinkingFrame : AgentFrame
{
    public required string Content { get; init; }
}

[MessagePackObject(keyAsPropertyName: true)]
public record MessageFrame : AgentFrame
{
    public required string Content { get; init; }
}

[MessagePackObject(keyAsPropertyName: true)]
public record InterruptFrame : AgentFrame
{
    public required string Reason { get; init; }
}

[MessagePackObject(keyAsPropertyName: true)]
public record AssignmentFrame : AgentFrame
{
    public required string NewRoomId { get; init; }
}

[MessagePackObject(keyAsPropertyName: true)]
public record DiscoveryFrame : AgentFrame
{
    public required List<RoomInfo> Rooms { get; init; }
}

[MessagePackObject(keyAsPropertyName: true)]
public record ToolRequestFrame : AgentFrame
{
    public required string RequestId { get; init; } 
    public required string ServerName { get; init; }
    public required string ToolName { get; init; }
    public required string ArgumentsJson { get; init; }
}

[MessagePackObject(keyAsPropertyName: true)]
public record ToolResponseFrame : AgentFrame
{
    public required string RequestId { get; init; }
    public bool Success { get; init; }
    public required string ResultJson { get; init; }
}

[MessagePackObject(keyAsPropertyName: true)]
public record RoomInfo
{
    public required string RoomId { get; init; }
    public string[] Tags { get; init; } = Array.Empty<string>();
    public int AgentCount { get; init; }
}

// SignalR Client Interface
public interface IAgentClient
{
    Task ReceiveSignal(AgentFrame frame);
}
