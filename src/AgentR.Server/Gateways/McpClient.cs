using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AgentR.Server.Gateways;

/// <summary>
/// A lightweight MCP Client that talks JSON-RPC over Stdio to a child process.
/// </summary>
public class McpClient : IDisposable
{
    private readonly Process _process;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, TaskCompletionSource<JsonNode>> _pendingRequests = new();
    
    // Limits concurrent requests to prevent flooding (Bulkhead Pattern)
    private readonly SemaphoreSlim _concurrencyLimiter = new(16, 16);
    // Serializes writes to the standard input stream (Thread Safety)
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    
    private readonly object _lock = new();
    private int _nextId = 1;
    private bool _isDisposed;
    private readonly ILogger _logger;

    public string ServerName { get; }

    public McpClient(string serverName, string command, string[] args, ILogger logger)
    {
        ServerName = serverName;
        _logger = logger;

        var psi = new ProcessStartInfo
        {
            FileName = command,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = AppContext.BaseDirectory // Default to app dir
        };

        foreach (var arg in args) psi.ArgumentList.Add(arg);

        _logger.LogInformation("[MCP:{Server}] Starting: {Command} {Args}", ServerName, command, string.Join(" ", args));

        _process = new Process { StartInfo = psi };
        
        _process.OutputDataReceived += (s, e) => HandleData(e.Data);
        _process.ErrorDataReceived += (s, e) => 
        {
            if (!string.IsNullOrWhiteSpace(e.Data)) 
                _logger.LogWarning("[MCP:{Server}] STDERR: {Data}", ServerName, e.Data);
        };

        _process.Start();
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
    }

    private void HandleData(string? data)
    {
        if (string.IsNullOrWhiteSpace(data)) return;

        try
        {
            var node = JsonNode.Parse(data);
            if (node == null) return;

            // Is it a response? keys: "jsonrpc", "id", "result"/"error"
            if (node["id"] != null)
            {
                var id = node["id"]?.ToString();
                if (id != null)
                {
                    if (_pendingRequests.TryRemove(id, out var tcs))
                    {
                        if (node["error"] != null)
                        {
                            var error = node["error"]?.ToString();
                            tcs.SetException(new Exception($"MCP Error: {error}"));
                        }
                        else
                        {
                             tcs.SetResult(node["result"] ?? new JsonObject());
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[MCP:{Server}] Failed to parse message: {Data}", ServerName, data);
        }
    }

    public async Task<string> CallToolAsync(string toolName, string argumentsJson, CancellationToken ct = default)
    {
        // 1. Bulkhead: Ensure we don't overload the process
        if (!await _concurrencyLimiter.WaitAsync(100, ct)) // Fail fast if full
        {
             throw new InvalidOperationException($"MCP Server '{ServerName}' is overloaded (Max 16 concurrent requests).");
        }

        try
        {
            // 2. Construct Request
            string id;
            lock(_lock) id = (_nextId++).ToString();

            var request = new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id,
                ["method"] = "tools/call",
                ["params"] = new JsonObject
                {
                    ["name"] = toolName,
                    ["arguments"] = JsonNode.Parse(argumentsJson)
                }
            };

            var tcs = new TaskCompletionSource<JsonNode>(TaskCreationOptions.RunContinuationsAsynchronously);
            
            _pendingRequests[id] = tcs;

            // 3. Serialize Output: Only one thread can write to stdin at a time
            // This prevents interleaved JSON frames
            await _writeLock.WaitAsync(ct);
            try
            {
                var json = request.ToJsonString();
                await _process.StandardInput.WriteLineAsync(json.AsMemory(), ct);
                await _process.StandardInput.FlushAsync(ct);
            }
            finally
            {
                _writeLock.Release();
            }

            // 4. Wait
            // Simple timeout wrapper
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(30));

            try 
            {
                var result = await tcs.Task.WaitAsync(timeoutCts.Token);
                
                // Result structure is { content: [ { type: "text", text: "..." } ] }
                if (result["content"] is JsonArray contentArray && contentArray.Count > 0)
                {
                     var textNode = contentArray.FirstOrDefault(x => x?["type"]?.GetValue<string>() == "text");
                     if (textNode != null)
                     {
                         return textNode["text"]?.GetValue<string>() ?? "";
                     }
                }
                
                return result.ToJsonString();
            }
            catch (Exception ex)
            {
                 _pendingRequests.TryRemove(id, out _);
                 throw new Exception($"MCP Call Failed: {ex.Message}", ex);
            }
        }
        finally
        {
            _concurrencyLimiter.Release();
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        try 
        {
            _process.Kill();
            _process.Dispose();
        }
        catch { }
    }
}
