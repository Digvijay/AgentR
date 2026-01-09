using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AgentR.Client;
using AgentR.Shared;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using StackExchange.Redis;

namespace AgentR.Worker;

public class WorkerBackgroundService : BackgroundService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly AgentClient _client;
    private readonly Kernel _kernel;
    private readonly ILogger<WorkerBackgroundService> _logger;
    
    // Config: What type of agent am I? (Usually passed via environment variable)
    private readonly string _agentType;

    public WorkerBackgroundService(
        IConnectionMultiplexer redis, 
        AgentClient client, 
        Kernel kernel,
        ILogger<WorkerBackgroundService> logger)
    {
        _redis = redis;
        _client = client;
        _kernel = kernel;
        _logger = logger;
        _agentType = Environment.GetEnvironmentVariable("AgentType") ?? "dotnet";
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var db = _redis.GetDatabase();
        var queueKey = $"agentr:lobby:{_agentType.ToLower()}";

        _logger.LogInformation("💤 {AgentType} Agent sleeping... Waiting for lobby ticket on {QueueKey}", _agentType, queueKey);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // 1. Blocking Pop (Wait for work)
                // We use ListLeftPopAsync. If empty, we wait/retry.
                // In a production KEDA setup, KEDA scales the pod UP because it sees items in the list.
                // The worker then pops the item and works.
                var result = await db.ListLeftPopAsync(queueKey);

                if (!result.HasValue) 
                {
                    // If no work, and we are running, it might mean another worker took it or we scaled up too much.
                    // We wait a bit to avoid CPU spin.
                    await Task.Delay(1000, stoppingToken);
                    continue; 
                }

                // 2. Work Found! Wake up!
                var ticket = JsonSerializer.Deserialize<AgentTicket>(result.ToString());
                if (ticket == null) continue;

                _logger.LogInformation("🚀 Waking up! Assigned to Room: {RoomId}", ticket.RoomId);

                // 3. Connect and Do the Job
                await ProcessJobAsync(ticket.RoomId, stoppingToken);
                
                // 4. After job is done, if the queue is empty, KEDA will eventually kill us.
                // We loop back to see if there's more work immediately.
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in WorkerBackgroundService loop");
                await Task.Delay(2000, stoppingToken);
            }
        }
    }

    private async Task ProcessJobAsync(string roomId, CancellationToken ct)
    {
        try
        {
            await _client.ConnectAsync();
            await _client.JoinRoomAsync(roomId);
            
            await _client.EmitThinkingAsync(roomId, "👋 I have arrived from cold storage to assist.");

            // Simulated reasoning/work
            // In a real scenario, we'd use Semantic Kernel here.
            var prompt = "Help the user in the room.";
            
            // We use a local CTS to ensure we can be interrupted by the network
            // BUT also respect the service's stopping token.
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            // Set up the turn behavior
            TaskCompletionSource<bool> tcs = new();
            
            _client.OnTurnGranted += async (turnCt) => 
            {
                using var turnLinkedCts = CancellationTokenSource.CreateLinkedTokenSource(linkedCts.Token, turnCt);
                try
                {
                    await _kernel.InvokeWithNetworkStreamingAsync(prompt, _client, roomId, turnLinkedCts.Token);
                    tcs.SetResult(true);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            };

            // Request turn to start working
            await _client.RequestTurnAsync(roomId);

            // Wait for completion or interruption
            await tcs.Task;

            await _client.EmitThinkingAsync(roomId, "✅ Job done. Going back to sleep.");
            await _client.LeaveRoomAsync(roomId);
            await _client.DisconnectAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process job for room {RoomId}", roomId);
        }
    }
}
