using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using AgentR.Server.Auditing;
using AgentR.Shared;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentR.Tests;

public class ResilienceVerificationTests
{
    // Fake Storage to simulate outages
    public class FakeFailingStorage : IAuditStorage
    {
        public int CallCount { get; private set; }
        public bool NeedsToFail { get; set; } = true;
        public List<AuditRecord> SavedRecords { get; } = new();

        public Task AppendRecordsAsync(string fileName, IEnumerable<AuditRecord> records, CancellationToken ct)
        {
            CallCount++;
            if (NeedsToFail)
            {
                NeedsToFail = false; // Fail only once, then succeed (simulate transient outage)
                throw new InvalidOperationException("Simulated Storage Outage 💥");
            }
            SavedRecords.AddRange(records);
            return Task.CompletedTask;
        }

        public Task<List<AuditRecord>> GetRecordsAsync(string roomId, CancellationToken ct) => Task.FromResult(new List<AuditRecord>());
    }

    [Fact]
    public async Task AuditService_ShouldSurviveCrash_AndRetainData()
    {
        // 1. Arrange
        var buffer = new AuditBuffer();
        var storage = new FakeFailingStorage();
        var logger = NullLogger<AuditService>.Instance;
        var service = new AuditService(buffer, storage, logger);

        var cts = new CancellationTokenSource();
        
        // 2. Act
        // Start the service
        var serviceTask = service.StartAsync(cts.Token);

        // 1. Wait for initial 5s window to pass to ensure Flush triggers immediately on first record
        await Task.Delay(TimeSpan.FromSeconds(6));

        // 2. Send Payload 1 - Should TRIGGER FLUSH (and FAIL)
        buffer.Record(new AuditRecord 
        { 
            RoomId = "resilience-room",
            AgentId = "tester",
            Content = "Payload 1" 
        });

        // 3. Wait a bit for the failure to happen
        await Task.Delay(TimeSpan.FromSeconds(1)); 
        
        // 4. Disable failure for the retry
        storage.NeedsToFail = false; 

        // 5. Send Payload 2 - Should TRIGGER FLUSH Again (Time > 5 is still true)
        buffer.Record(new AuditRecord { 
             RoomId="resilience-room", AgentId="tester", Content = "Payload 2" 
        });

        // 6. Allow flush to complete
        await Task.Delay(TimeSpan.FromSeconds(2));
        // It fails, waits 5s, then retries.
        // T=0 Start, T=5 Fail, T=5 Backoff -> T=10, T=15 Next Flush.
        // Total wait: ~18 seconds should cover it.
        await Task.Delay(TimeSpan.FromSeconds(18));

        // Stop
        await service.StopAsync(CancellationToken.None);

        // 3. Assert
        // CallCount should be at least 2:
        // 1st attempt -> Crash
        // 5s delay
        // 2nd attempt -> Success (or Final Flush success)
        
        Assert.True(storage.CallCount >= 2, $"Service should have retried. Calls: {storage.CallCount}");
        Assert.Contains(storage.SavedRecords, r => r.Content == "Payload 1");
    }
}
