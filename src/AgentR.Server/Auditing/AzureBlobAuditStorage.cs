using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Specialized;
using AgentR.Shared;
using Microsoft.Extensions.Configuration;

namespace AgentR.Server.Auditing;

/// <summary>
/// A storage provider that persists audit records to Azure Blob Storage using Append Blobs.
/// Ideal for high-scale, immutable long-term retention ("Cold Storage").
/// </summary>
public class AzureBlobAuditStorage : IAuditStorage
{
    private readonly BlobContainerClient _containerClient;

    /// <summary>
    /// Initializes a new instance of <see cref="AzureBlobAuditStorage"/>.
    /// </summary>
    /// <param name="configuration">Configuration containing connection strings.</param>
    public AzureBlobAuditStorage(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("AuditStorage");
        var containerName = configuration["AuditContainerName"] ?? "flight-logs";
        
        var blobServiceClient = new BlobServiceClient(connectionString);
        _containerClient = blobServiceClient.GetBlobContainerClient(containerName);
        _containerClient.CreateIfNotExists();
    }

    public async Task AppendRecordsAsync(string fileName, IEnumerable<AuditRecord> records, CancellationToken ct)
    {
        var blobClient = _containerClient.GetBlobClient(fileName);
        
        // Note: Azure Blobs don't support simple append easily without "Append Blobs"
        // For simplicity in this demo, we'll use a Block Blob and overwrite/concatenate 
        // OR better, we use AppendBlobs if available.
        // In real enterprise, specialized logging sinks are preferred.
        
        var appendBlobClient = _containerClient.GetAppendBlobClient(fileName);
        await appendBlobClient.CreateIfNotExistsAsync(cancellationToken: ct);

        using var ms = new MemoryStream();
        using var writer = new StreamWriter(ms);
        foreach (var record in records)
        {
            await writer.WriteLineAsync(JsonSerializer.Serialize(record));
        }
        await writer.FlushAsync(ct);
        ms.Position = 0;

        await appendBlobClient.AppendBlockAsync(ms, cancellationToken: ct);
    }

    public async Task<List<AuditRecord>> GetRecordsAsync(string roomId, CancellationToken ct)
    {
        var logs = new List<AuditRecord>();

        await foreach (var blobItem in _containerClient.GetBlobsAsync(cancellationToken: ct))
        {
            var blobClient = _containerClient.GetBlobClient(blobItem.Name);
            var response = await blobClient.DownloadAsync(ct);
            using var reader = new StreamReader(response.Value.Content);
            
            while (!reader.EndOfStream)
            {
                var line = await reader.ReadLineAsync(ct);
                if (string.IsNullOrWhiteSpace(line)) continue;

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
