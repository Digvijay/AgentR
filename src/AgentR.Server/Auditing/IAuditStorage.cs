using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgentR.Shared;

namespace AgentR.Server.Auditing;

/// <summary>
/// Defines the storage provider for the Flight Recorder auditing system.
/// </summary>
public interface IAuditStorage
{
    /// <summary>
    /// Appends a batch of records to a specific storage file or blob.
    /// </summary>
    /// <param name="fileName">The destination name (e.g., log_2024-01-01.jsonl).</param>
    /// <param name="records">The audit records to persist.</param>
    /// <param name="ct">Cancellation token.</param>
    Task AppendRecordsAsync(string fileName, IEnumerable<AuditRecord> records, CancellationToken ct);

    /// <summary>
    /// Retrieves all audit records for a specific room across all storage artifacts.
    /// </summary>
    /// <param name="roomId">The unique identifier of the room.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A collection of AuditRecord objects sorted by timestamp.</returns>
    Task<List<AuditRecord>> GetRecordsAsync(string roomId, CancellationToken ct);
}
