using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using AgentR.Shared;

namespace AgentR.Server.Auditing;

/// <summary>
/// A background service that drains the <see cref="AuditBuffer"/> and persists records 
/// to the configured <see cref="IAuditStorage"/>.
/// handles batching, periodic flushing, and resilience.
/// </summary>
public class AuditService : BackgroundService
{
    private readonly AuditBuffer _buffer;
    private readonly IAuditStorage _storage;
    private readonly ILogger<AuditService> _logger;

    public AuditService(AuditBuffer buffer, IAuditStorage storage, ILogger<AuditService> logger)
    {
        _buffer = buffer;
        _storage = storage;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("📼 Flight Recorder Started.");

        var batch = new List<AuditRecord>(100);
        var lastFlush = DateTime.UtcNow;

        try
        {
            await foreach (var record in _buffer.ReadAllAsync(stoppingToken))
            {
                batch.Add(record);

                if (batch.Count >= 100 || (DateTime.UtcNow - lastFlush).TotalSeconds >= 5)
                {
                    await FlushBatchAsync(batch, stoppingToken);
                    lastFlush = DateTime.UtcNow;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Critical error in AuditService loop!");
        }
        finally
        {
            // Final flush
            if (batch.Count > 0)
            {
                await FlushBatchAsync(batch, CancellationToken.None);
            }
        }
    }

    private async Task FlushBatchAsync(List<AuditRecord> batch, CancellationToken ct)
    {
        try
        {
            var fileName = $"log_{DateTime.UtcNow:yyyy-MM-dd}.jsonl";
            await _storage.AppendRecordsAsync(fileName, batch, ct);
            batch.Clear();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to flush audit logs!");
        }
    }
}
