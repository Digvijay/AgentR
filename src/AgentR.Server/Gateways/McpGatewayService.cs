using Microsoft.Extensions.Logging;
using System.Text.Json.Nodes;

namespace AgentR.Server.Gateways;

public class McpGatewayService : IDisposable
{
    private readonly Dictionary<string, McpClient> _clients = new();
    private readonly ILogger<McpGatewayService> _logger;

    public McpGatewayService(ILogger<McpGatewayService> logger, IConfiguration configuration)
    {
        _logger = logger;
        
        // Load mcp.json
        // Ideally checking configuration["mcp:configPath"] or defaulting to root
        var configPath = Path.Combine(Directory.GetCurrentDirectory(), "../../../../mcp.json");
        // For robustness in this demo environment, try a few paths
        if (!File.Exists(configPath)) configPath = Path.Combine(AppContext.BaseDirectory, "mcp.json");
        if (!File.Exists(configPath)) configPath = "/Users/digvijay/source/github/AgentR/mcp.json"; // Hardcoded backup for demo

        if (File.Exists(configPath))
        {
            _logger.LogInformation("Loading MCP Config from: {Path}", configPath);
            try 
            {
                var json = File.ReadAllText(configPath);
                var root = JsonNode.Parse(json);
                var servers = root?["mcpServers"]?.AsObject();
                
                if (servers != null)
                {
                    foreach (var kvp in servers)
                    {
                        var name = kvp.Key;
                        var cmd = kvp.Value?["command"]?.ToString();
                        var argsNode = kvp.Value?["args"]?.AsArray();
                        var args = argsNode?.Select(n => n?.ToString() ?? "").ToArray() ?? Array.Empty<string>();

                        if (!string.IsNullOrEmpty(cmd))
                        {
                            try 
                            {
                                _clients[name] = new McpClient(name, cmd, args, logger);
                            }
                            catch (Exception ex)
                            {
                                logger.LogError(ex, "Failed to start MCP Server: {Name}", name);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse mcp.json");
            }
        }
        else
        {
            _logger.LogWarning("mcp.json not found at {Path}", configPath);
        }
    }

    public async Task<string> ExecuteToolAsync(string serverName, string toolName, string args, CancellationToken ct = default)
    {
        if (_clients.TryGetValue(serverName, out var client))
        {
            _logger.LogInformation("Routing tool request {ToolName} to MCP server {ServerName}", toolName, serverName);
            return await client.CallToolAsync(toolName, args, ct);
        }

        // Fallback or Error
        throw new KeyNotFoundException($"MCP Server '{serverName}' not configured or failed to start.");
    }

    public void Dispose()
    {
        foreach (var client in _clients.Values)
        {
            client.Dispose();
        }
        _clients.Clear();
    }
}
