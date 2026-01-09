using AgentR.Client;
using AgentR.Shared;
using Microsoft.SemanticKernel;

namespace AgentR.Demo.Workers;

/// <summary>
/// CodeWriter Agent - Writes code based on requirements
/// </summary>
public class CodeWriterWorker : BackgroundService
{
    private readonly AgentClient _client;
    private readonly Kernel _kernel;
    private readonly ILogger<CodeWriterWorker> _logger;
    private readonly string _roomId = "code-review-demo-room";
    private readonly string _agentId;

    public CodeWriterWorker(Func<string, AgentClient> clientFactory, Kernel kernel, ILogger<CodeWriterWorker> logger)
    {
        _agentId = $"code-writer";
        _client = clientFactory(_agentId);
        _kernel = kernel;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("CodeWriter agent {AgentId} starting...", _agentId);

        // Set up event handler for incoming messages
        _client.OnSignalReceived += async frame =>
        {
            if (frame is MessageFrame msg && msg.Content.StartsWith("TASK:"))
            {
                await HandleTaskAsync(msg.Content.Replace("TASK:", "").Trim());
            }
        };

        // Connect and join the room
        await _client.ConnectAsync(stoppingToken);
        await _client.JoinRoomAsync(_roomId, tags: new[] { "code-writer" }, ct: stoppingToken);
        
        _logger.LogInformation("CodeWriter agent ready in room {RoomId}", _roomId);

        // Keep running
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task HandleTaskAsync(string task)
    {
        try
        {
            _logger.LogInformation("CodeWriter received task: {Task}", task);
            
            await _client.EmitMessageAsync(_roomId, $"[CodeWriter] Analyzing task: {task}");

            string code;
            try
            {
                // Try to use Semantic Kernel to generate code
                var prompt = $@"You are a professional software developer. Write clean, well-documented code for the following task:

Task: {task}

Provide the code in a single code block with proper comments. Keep it simple and functional.";

                var response = await _kernel.InvokePromptAsync(prompt);
                code = response.ToString();
            }
            catch (Exception llmEx)
            {
                // Fallback to mock response if LLM is unavailable
                _logger.LogWarning("LLM unavailable, using mock response: {Error}", llmEx.Message);
                code = GenerateMockCode(task);
            }

            await _client.EmitMessageAsync(_roomId, $"[CodeWriter] Code generated:\n```csharp\n{code}\n```");
            
            // Pass to reviewer
            await _client.EmitMessageAsync(_roomId, "REVIEW_REQUEST:" + code);
            
            _logger.LogInformation("CodeWriter completed task and sent to reviewer");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in CodeWriter");
            await _client.EmitMessageAsync(_roomId, $"[CodeWriter] Error: {ex.Message}");
        }
    }

    private string GenerateMockCode(string task)
    {
        // Simple mock code generator for demo purposes
        if (task.ToLower().Contains("fibonacci"))
        {
            return @"public class FibonacciCalculator
{
    /// <summary>
    /// Calculates the nth Fibonacci number using iterative approach
    /// </summary>
    public static long Calculate(int n)
    {
        if (n <= 1) return n;
        
        long prev = 0, curr = 1;
        for (int i = 2; i <= n; i++)
        {
            long next = prev + curr;
            prev = curr;
            curr = next;
        }
        return curr;
    }
}";
        }
        else if (task.ToLower().Contains("sort") || task.ToLower().Contains("array"))
        {
            return @"public class ArraySorter
{
    /// <summary>
    /// Sorts an array using QuickSort algorithm
    /// </summary>
    public static void QuickSort(int[] arr, int low, int high)
    {
        if (low < high)
        {
            int pi = Partition(arr, low, high);
            QuickSort(arr, low, pi - 1);
            QuickSort(arr, pi + 1, high);
        }
    }
    
    private static int Partition(int[] arr, int low, int high)
    {
        int pivot = arr[high];
        int i = low - 1;
        
        for (int j = low; j < high; j++)
        {
            if (arr[j] < pivot)
            {
                i++;
                (arr[i], arr[j]) = (arr[j], arr[i]);
            }
        }
        (arr[i + 1], arr[high]) = (arr[high], arr[i + 1]);
        return i + 1;
    }
}";
        }
        else
        {
            return $@"public class TaskHandler
{{
    /// <summary>
    /// Implementation for: {task}
    /// </summary>
    public void Execute()
    {{
        // TODO: Implement {task}
        Console.WriteLine(""Task: {task}"");
    }}
}}";
        }
    }
}
