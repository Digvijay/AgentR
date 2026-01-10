# Flight Recorder (Auditing & Compliance)

The Flight Recorder provides an immutable audit log of all conversation frames, essential for enterprise compliance, auditing, and debugging AI hallucinations.

## Key Components

### 📼 High-Speed Buffer (`AuditBuffer.cs`)
Uses `System.Threading.Channels` to capture events (Thinking, Messages, Requests) without blocking the SignalR hub execution. It employs a bounded capacity (10,000) with `DropWrite` to prioritize server stability.

### ☁️ Pluggable Storage (`IAuditStorage.cs`)
A storage abstraction that decouples the server from the underlying persistence layer.
- **Local Storage**: Writes JSON Lines (`.jsonl`) to the local file system.
- **Azure Blob Storage**: Efficiently appends logs to Azure Block/Append Blobs for long-term "cold" storage.

### ⚙️ Background worker (`AuditService.cs`)
Drains the `AuditBuffer`, batches records (up to 100 or every 5 seconds), and flushes them to the configured `IAuditStorage`.

### 🛡️ Hub Filter (`AuditFilter.cs`)
Intercepts `Broadcast` and `RequestTurn` calls to automatically capture the context and content of every interaction. 
> [!NOTE]
> This includes failed attempts due to **FinOps / Budget** rejections, ensuring a full audit trail of "denied" agent activity.

## Performance & Resilience
- **Batching**: Reduces disk/network IO by grouping records.
- **Periodic Flushing**: Ensures data is persisted even during low-traffic periods.
- **Non-blocking**: The "Fast Path" (SignalR) never waits for the "Slow Path" (Disk/Cloud).
- **Immortal Loop**: The `AuditService` catches unhandled exceptions and restarts its loop (with backoff), ensuring auditing never silently dies.
- **Resilient Flush**: Failed batches are retained in memory and retried, preventing data loss during transient storage outages.

## Configuration
To enable Azure Blob Storage, add the following to your `ConnectionStrings`:
```json
{
  "ConnectionStrings": {
    "AuditStorage": "DefaultEndpointsProtocol=https;AccountName=...;AccountKey=...;EndpointSuffix=core.windows.net"
  },
  "AuditContainerName": "flight-recorder-logs"
}
```
If no connection string is provided, the system defaults to local file storage.

## Session Replay
The platform provides a foundation for replaying sessions via the `/replay/{roomId}` endpoint, which aggregates logs from the configured storage provider. A premium UI implementation can be found in `samples/AgentR.Demo`.
