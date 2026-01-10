# KEDA Autoscaling (Scale-to-Zero)

This document outlines the technical implementation of the **Lobby Pattern** in AgentR, which enables event-driven autoscaling of agent containers to zero when idle.

## Architecture

AgentR uses a high-performance Redis-backed "Lobby" to orchestrate just-in-time agent provisioning.

### The "Long-Running" FaaS Model

This implementation effectively transforms AgentR from an "Always-On Service" into a **Serverless FaaS (Function as a Service)** architecture, but with a critical advantage: it removes the traditional constraints of serverless platforms (like AWS Lambda).

| Feature | Legacy AgentR | AgentR (KEDA) | Traditional FaaS |
| :--- | :--- | :--- | :--- |
| **Idle State** | Always running | **Scale-to-Zero** | Scale-to-Zero |
| **Boutique Power** | Full Containers | **Full Containers** | Restricted CPU/RAM |
| **Execution Time** | Unlimited | **Unlimited** | Limited (e.g., 15 mins) |
| **Cost Model** | Monthly / Per-Node | **On-Demand** | On-Demand |

By combining the **Lobby Pattern** with KEDA, you gain the cost efficiency of serverless without sacrificing the "Stateful" and "Long-Lived" nature required by complex AI Agents.

### 1. The Lobby Pattern
Instead of agents being "Always-On", they are requested on-demand:
- **Hub (Dispatcher)**: When a user or system requests an agent via `RequestAgentAsync`, the Hub serializes an `AgentTicket` and pushes it to a Redis List (`agentr:lobby:{type}`).
- **KEDA (Scaler)**: A Kubernetes ScaledObject monitors the length of this list.
- **Worker (Consumer)**: Once KEDA spins up a pod, the worker uses `WorkerBackgroundService` to perform a blocking `BLPOP` on the Redis list, retrieves the ticket, and joins the room via SignalR.

## Implementation Details

### Shared Protocol (`AgentR.Shared`)
The `AgentTicket` record defines the contract for work assignments:
```csharp
public record AgentTicket
{
    public string RoomId { get; init; } = string.Empty;
    public DateTime RequestedAt { get; init; } = DateTime.UtcNow;
    public Dictionary<string, string>? Metadata { get; init; }
}
```

### Hub logic (`AgentR.Server`)
The `AgentHub` now acts as a dispatcher:
```csharp
public async Task RequestAgent(string agentType)
{
    var ticket = new AgentTicket { RoomId = currentRoomId };
    var queueKey = $"agentr:lobby:{agentType.ToLower()}";
    await _redis.ListRightPushAsync(queueKey, JsonSerializer.Serialize(ticket));
}
```

### Worker logic (`AgentR.Worker`)
The worker is refactored from a long-running process to a task-oriented background service:
- **Idle**: 0 pods running (Cost = $0).
- **Active**: Worker connects, performs `InvokeWithNetworkStreamingAsync`, releases the turn, leaves the room, and disconnects.

## Deployment (Kubernetes + KEDA)

View the full manifest in `k8s/agentr-keda.yaml`.

### ScaledObject Snippet
```yaml
spec:
  scaleTargetRef:
    name: agentr-worker-dotnet
  minReplicaCount: 0
  triggers:
  - type: redis
    metadata:
      address: "redis:6379"
      listName: "agentr:lobby:dotnet"
      listLength: "1"
```

## How it was tested
- **End-to-End Flow**: Successfully local-simulated "Lobby -> Wake up -> SignalR Join -> Turn Acquisition".
- **Resilience**: Verified that accidental disconnects during a KEDA session automatically release the distributed turn lock.
