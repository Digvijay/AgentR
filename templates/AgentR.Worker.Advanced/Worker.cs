using AgentR.Client;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

public class Worker : BackgroundService
{
    private readonly AgentClient _client;
    private readonly ILogger<Worker> _logger;
    private readonly SystemInfoTool _systemTool;

    public Worker(AgentClient client, ILogger<Worker> logger, SystemInfoTool systemTool)
    {
        _client = client;
        _logger = logger;
        _systemTool = systemTool;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 1. Hook Events
        _client.OnTurnGranted += HandleTurnAsync;
        _client.OnToolRequest += HandleToolRequestAsync;

        try 
        {
            // 2. Connect
            _logger.LogInformation("🔌 Connecting to AgentR Hub...");
            await _client.ConnectAsync(stoppingToken);

            // 3. Join with Semantic Tags (Discovery)
            // Tags allow the Dispatcher to route requests to us based on capabilities
            var tags = new[] { "sysadmin", "tier-2", "gpu-enabled" };
            await _client.JoinRoomAsync("General", tags, stoppingToken);
            
            _logger.LogInformation("✅ Joined 'General' with tags: {Tags}", string.Join(", ", tags));

            // 4. Main Event Loop
            while (!stoppingToken.IsCancellationRequested)
            {
                // In a real agent, you might pull from a queue or wait for an explicit "Wake" signal
                // For this demo, we interactively request a turn every 10 seconds to simulate activity
                await Task.Delay(10000, stoppingToken);
                await _client.RequestTurnAsync("General", stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("🛑 Worker stopping gracefully...");
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "💥 Fatal worker error");
        }
    }

    private async Task HandleTurnAsync(CancellationToken ct)
    {
        _logger.LogInformation("🎤 Turn granted. Running inference...");

        // FinOps: Check Budget before expensive operations
        var balance = await _client.GetBalanceAsync("General", ct);
        if (balance < 10)
        {
            _logger.LogWarning("⚠️ Low Credit Balance ({Balance}). Switching to cheap model.", balance);
            await _client.EmitThinkingAsync("General", "Budget low. Using localized logic.", ct);
        }
        else
        {
            await _client.EmitThinkingAsync("General", "Budget healthy. Analyzing system state...", ct);
        }

        // Simulate work
        await Task.Delay(1000, ct);
        var sysInfo = _systemTool.GetInfo();
        await _client.EmitMessageAsync("General", $"Report: {sysInfo}", ct);
        
        _logger.LogInformation("✅ Turn complete.");
    }

    private async Task<string> HandleToolRequestAsync(string toolName, string args, CancellationToken ct)
    {
        _logger.LogInformation("🛠️ Tool Request: {Tool}", toolName);
        
        if (toolName == "get_system_info")
        {
            return _systemTool.GetInfo();
        }

        return "Error: Unknown tool";
    }
}
