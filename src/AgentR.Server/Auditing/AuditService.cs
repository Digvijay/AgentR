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

        // "Immortal" Loop
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // We restart reading from the buffer where we left off (it's a channel)
                await foreach (var record in _buffer.ReadAllAsync(stoppingToken))
                {
                    batch.Add(record);

                    if (batch.Count >= 100 || (DateTime.UtcNow - lastFlush).TotalSeconds >= 5)
                    {
                        if (await TryFlushBatchAsync(batch, stoppingToken))
                        {
                            lastFlush = DateTime.UtcNow;
                        }
                        // If flush failed, we keep the items in 'batch' and try again next loop/item
                        // We continue reading to avoid blocking the channel if possible, 
                        // OR we could break inner loop to force a retry delay. 
                        // For safety, let's just keep accumulating until specific limits or retry works.
                        // Ideally, if storage is down, we might eventually OOM if we don't drop.
                        // Simple policy: If flush failed, just carry over.
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Critical error in AuditService loop! Restarting in 5s...");
                try 
                {
                    await Task.Delay(5000, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        // Final shutdown flush attempt
        if (batch.Count > 0)
        {
            await TryFlushBatchAsync(batch, CancellationToken.None);
        }
    }

    private async Task<bool> TryFlushBatchAsync(List<AuditRecord> batch, CancellationToken ct)
    {
        if (batch.Count == 0) return true;

        try
        {
            var fileName = $"log_{DateTime.UtcNow:yyyy-MM-dd}.jsonl";
            await _storage.AppendRecordsAsync(fileName, batch, ct);
            batch.Clear(); // Only clear on success
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to flush audit logs! Retaining {Count} records for retry.", batch.Count);
            return false;
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
