using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AgentR.Shared;

namespace AgentR.Server.Auditing;

/// <summary>
/// A storage provider that persists audit records to the local file system using JSON Lines (.jsonl).
/// </summary>
public class LocalAuditStorage : IAuditStorage
{
    private readonly string _logDirectory;

    /// <summary>
    /// Initializes a new instance of <see cref="LocalAuditStorage"/>.
    /// </summary>
    /// <param name="logDirectory">The directory where log files will be created.</param>
    public LocalAuditStorage(string logDirectory = "flight_data")
    {
        _logDirectory = logDirectory;
        if (!Directory.Exists(_logDirectory))
        {
            Directory.CreateDirectory(_logDirectory);
        }
    }

    public async Task AppendRecordsAsync(string fileName, IEnumerable<AuditRecord> records, CancellationToken ct)
    {
        var filePath = Path.Combine(_logDirectory, fileName);
        var lines = records.Select(r => JsonSerializer.Serialize(r));
        await File.AppendAllLinesAsync(filePath, lines, ct);
    }

    public async Task<List<AuditRecord>> GetRecordsAsync(string roomId, CancellationToken ct)
    {
        var logs = new List<AuditRecord>();
        if (!Directory.Exists(_logDirectory)) return logs;

        var logFiles = Directory.GetFiles(_logDirectory, "*.jsonl");
        foreach (var file in logFiles)
        {
            var lines = await File.ReadAllLinesAsync(file, ct);
            foreach (var line in lines)
            {
                var record = JsonSerializer.Deserialize<AuditRecord>(line);
                if (record?.RoomId == roomId)
                {
                    logs.Add(record);
                }
            }
        }

        return logs.OrderBy(l => l.Timestamp).ToList();
    }
}
