using Microsoft.Extensions.Logging;

namespace AgentR.Server.Gateways;

public class McpGatewayService
{
    private readonly Dictionary<string, Func<string, string, CancellationToken, Task<string>>> _toolRegistry;
    private readonly ILogger<McpGatewayService> _logger;

    public McpGatewayService(ILogger<McpGatewayService> logger)
    {
        _logger = logger;
        _toolRegistry = new();

        _toolRegistry["github"] = (tool, args, ct) =>
        {
            if (tool == "get_issue")
            {
                return Task.FromResult("""{ "id": 42, "title": "Add MCP Support", "status": "open" }""");
            }
            throw new ArgumentException($"Unknown tool: {tool}");
        };
        
        _toolRegistry["weather"] = (tool, args, ct) =>
        {
            if (tool == "get_forecast")
            {
                return Task.FromResult("""{ "location": "San Francisco", "temp": 72, "unit": "F" }""");
            }
            throw new ArgumentException($"Unknown tool: {tool}");
        };
    }

    public async Task<string> ExecuteToolAsync(string serverName, string toolName, string args, CancellationToken ct = default)
    {
        if (_toolRegistry.TryGetValue(serverName, out var handler))
        {
            _logger.LogInformation("Routing tool request {ToolName} to MCP server {ServerName}", toolName, serverName);
            return await handler(toolName, args, ct);
        }

        throw new KeyNotFoundException($"MCP Server '{serverName}' not found.");
    }
}
