using System.Threading.Channels;
using AgentR.Shared;

namespace AgentR.Server.Auditing;

/// <summary>
/// A high-performance, non-blocking producer/consumer buffer for AuditRecord objects.
/// Uses System.Threading.Channels to safely pass records from the SignalR Hub to the background writer.
/// </summary>
public class AuditBuffer
{
    private readonly Channel<AuditRecord> _channel = Channel.CreateBounded<AuditRecord>(new BoundedChannelOptions(10000)
    {
        FullMode = BoundedChannelFullMode.DropWrite // Prioritize Hub availability over logging during extreme load
    });

    /// <summary>
    /// Adds a record to the buffer for eventual persistence.
    /// </summary>
    public void Record(AuditRecord record)
    {
        // "TryWrite" is non-blocking. If buffer is full, we drop the record.
        _channel.Writer.TryWrite(record);
    }

    public bool TryRead(out AuditRecord? record)
    {
        return _channel.Reader.TryRead(out record);
    }

    public IAsyncEnumerable<AuditRecord> ReadAllAsync(CancellationToken ct)
    {
        return _channel.Reader.ReadAllAsync(ct);
    }
}
