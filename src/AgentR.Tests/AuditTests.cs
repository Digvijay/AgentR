using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgentR.Server.Auditing;
using AgentR.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AgentR.Tests;

public class AuditTests : IDisposable
{
    private const string TestLogDir = "test_flight_data";

    public AuditTests()
    {
        if (Directory.Exists(TestLogDir)) Directory.Delete(TestLogDir, true);
    }

    public void Dispose()
    {
        if (Directory.Exists(TestLogDir)) Directory.Delete(TestLogDir, true);
    }

    [Fact]
    public async Task AuditBuffer_ShouldRecordAndRead()
    {
        // Arrange
        var buffer = new AuditBuffer();
        var record = new AuditRecord
        {
            RoomId = "test-room",
            AgentId = "test-agent",
            EventType = "TestEvent",
            Content = "Hello"
        };

        // Act
        buffer.Record(record);

        // Assert
        var cts = new CancellationTokenSource(1000);
        await foreach (var item in buffer.ReadAllAsync(cts.Token))
        {
            Assert.Equal("test-room", item.RoomId);
            Assert.Equal("Hello", item.Content);
            break;
        }
    }

    [Fact]
    public async Task AuditService_ShouldBatchAndFlushToStorage()
    {
        // Arrange
        var buffer = new AuditBuffer();
        var storage = new LocalAuditStorage(TestLogDir); // Use isolated dir
        var logger = NullLogger<AuditService>.Instance;
        var service = new AuditService(buffer, storage, logger);
        
        var record = new AuditRecord
        {
            RoomId = "test-room-batch",
            AgentId = "test-agent",
            EventType = "BatchTest",
            Content = "Batch Persistence"
        };

        var cts = new CancellationTokenSource();
        
        // Start service in background
        await service.StartAsync(cts.Token);

        // Act
        buffer.Record(record);
        
        // Wait for service to process (it flushes every 5s or 100 items)
        // For testing, we might want to reduce the flush interval or just wait.
        // Since we want to be "bullet-proof", let's wait a bit longer or trigger shutdown flush.
        await Task.Delay(500); 
        
        // Stop service triggers final flush
        await service.StopAsync(cts.Token);

        // Assert
        var logs = await storage.GetRecordsAsync("test-room-batch", default);
        Assert.Single(logs);
        Assert.Equal("Batch Persistence", logs[0].Content);
    }

    [Fact]
    public async Task LocalAuditStorage_ShouldPersistAndRetrieve()
    {
        // Arrange
        var storage = new LocalAuditStorage(TestLogDir); // Use isolated dir
        var records = new[] {
            new AuditRecord { RoomId = "room-1", AgentId = "A", EventType = "T", Content = "C1" },
            new AuditRecord { RoomId = "room-1", AgentId = "B", EventType = "T", Content = "C2" },
            new AuditRecord { RoomId = "room-2", AgentId = "C", EventType = "T", Content = "C3" }
        };

        // Act
        await storage.AppendRecordsAsync("test_persistence.jsonl", records, default);

        // Assert
        var room1Logs = await storage.GetRecordsAsync("room-1", default);
        var room2Logs = await storage.GetRecordsAsync("room-2", default);

        Assert.Equal(2, room1Logs.Count);
        Assert.Single(room2Logs);
        Assert.Equal("C3", room2Logs[0].Content);
    }
    [Fact]
    public async Task LocalAuditStorage_ShouldFilterByRoom()
    {
        // Arrange
        var storage = new LocalAuditStorage(TestLogDir);
        await storage.AppendRecordsAsync("mixed.jsonl", new[] {
            new AuditRecord { RoomId = "A", AgentId = "Test", Content = "1" },
            new AuditRecord { RoomId = "B", AgentId = "Test", Content = "2" }
        }, default);

        // Act
        var logsA = await storage.GetRecordsAsync("A", default);
        var logsB = await storage.GetRecordsAsync("B", default);

        // Assert
        Assert.Single(logsA);
        Assert.Single(logsB);
        Assert.Equal("1", logsA[0].Content);
        Assert.Equal("2", logsB[0].Content);
    }
}
