using AgentR.Client;
using AgentR.Shared;
using Microsoft.SemanticKernel;

namespace AgentR.Demo.Workers;

/// <summary>
/// CodeReviewer Agent - Reviews code for quality and bugs
/// </summary>
public class CodeReviewerWorker : BackgroundService
{
    private readonly AgentClient _client;
    private readonly Kernel _kernel;
    private readonly ILogger<CodeReviewerWorker> _logger;
    private readonly string _roomId = "code-review-demo-room";
    private readonly string _agentId;

    public CodeReviewerWorker(Func<string, AgentClient> clientFactory, Kernel kernel, ILogger<CodeReviewerWorker> logger)
    {
        _agentId = $"code-reviewer";
        _client = clientFactory(_agentId);
        _kernel = kernel;
        _logger = logger;
    }

    private string? _pendingCode;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("CodeReviewer agent {AgentId} starting...", _agentId);

        _client.OnTurnGranted += async ct =>
        {
            if (!string.IsNullOrEmpty(_pendingCode))
            {
                var code = _pendingCode;
                _pendingCode = null;
                await HandleReviewInternalAsync(code, ct);
            }
        };

        _client.OnSignalReceived += async frame =>
        {
            if (frame is MessageFrame msg)
            {
                if (msg.Content.StartsWith("REVIEW_REQUEST:") || msg.Content.StartsWith("REVIEW_REQUEST_WITH_CONFIG|"))
                {
                    _pendingCode = msg.Content;
                    _logger.LogInformation("CodeReviewer queued review, requesting turn...");
                    try
                    {
                        await _client.RequestTurnAsync(_roomId, stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning("Failed to request turn: {Message}", ex.Message);
                    }
                }
                else if (msg.SenderId == "HOST" && msg.Content.Contains("BUSY:"))
                {
                    _logger.LogWarning("Room busy. Waiting for floor to open...");
                }
            }
            else if (frame is FloorOpenFrame)
            {
                if (!string.IsNullOrEmpty(_pendingCode))
                {
                    _logger.LogInformation("Floor is open. Requesting turn for pending review...");
                    try
                    {
                        await _client.RequestTurnAsync(_roomId, stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning("Failed to request turn: {Message}", ex.Message);
                    }
                }
            }
        };

        await _client.ConnectAsync(stoppingToken);
        await _client.JoinRoomAsync(_roomId, tags: new[] { "code-reviewer" }, ct: stoppingToken);
        
        _logger.LogInformation("CodeReviewer agent ready in room {RoomId}", _roomId);

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private class LlmConfig { public string endpoint { get; set; } public string key { get; set; } public string model { get; set; } }

    private async Task HandleReviewInternalAsync(string rawMessage, CancellationToken ct)
    {
        string code = rawMessage;
        string? configJson = null;
        Kernel kernelToUse = _kernel;

        if (rawMessage.StartsWith("REVIEW_REQUEST_WITH_CONFIG|"))
        {
            try 
            {
                var parts = rawMessage.Split('|', 3);
                if (parts.Length == 3)
                {
                    var json = parts[1];
                    configJson = json;
                    code = parts[2];
                    var config = System.Text.Json.JsonSerializer.Deserialize<LlmConfig>(json, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    
                    if (config != null)
                    {
                        var builder = Kernel.CreateBuilder();
                        builder.AddOpenAIChatCompletion(
                            modelId: config.model,
                            apiKey: config.key,
                            httpClient: new HttpClient { BaseAddress = new Uri(config.endpoint) }
                        );
                        kernelToUse = builder.Build();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse dynamic config in Reviewer");
                await _client.EmitMessageAsync(_roomId, $"[System] ⚠️ Config Parse Error: {ex.Message}", ct);
            }
        }
        else if (rawMessage.StartsWith("REVIEW_REQUEST:"))
        {
            code = rawMessage.Replace("REVIEW_REQUEST:", "").Trim();
        }

        try
        {
            _logger.LogInformation("CodeReviewer analyzing code...");
            
            await _client.EmitMessageAsync(_roomId, "[CodeReviewer] Reviewing code for quality and bugs...", ct);

            // Simulate analysis delay
            await Task.Delay(2500, ct);

            string review;
            try
            {
                var prompt = $@"You are a senior code reviewer. Review the following code and provide:
1. Overall assessment (APPROVED or NEEDS_CHANGES)
2. List of issues found (if any)
3. Suggestions for improvement

Code to review:
{code}

Keep your review concise and actionable.";

                var response = await kernelToUse.InvokePromptAsync(prompt);
                review = response.ToString();
            }
            catch (Exception llmEx)
            {
                _logger.LogWarning("LLM unavailable, using mock review: {Error}", llmEx.Message);
                await _client.EmitMessageAsync(_roomId, $"[System] ⚠️ Reviewer LLM Failed: {llmEx.Message}", ct);
                review = @"**Code Review Results**

✅ **APPROVED** (Fallback Mock)

**Strengths:**
- Clean, readable code structure
- Proper use of XML documentation comments
- Efficient algorithm implementation
- Good variable naming conventions

**Minor Suggestions:**
- Consider adding input validation
- Could add unit tests for edge cases

**Verdict:** Code is production-ready with minor improvements suggested.";
            }

            await _client.EmitMessageAsync(_roomId, $"[CodeReviewer] Review complete:\n{review}", ct);
            
            // Pass to tester if approved
            if (review.Contains("APPROVED"))
            {
                if (configJson != null)
                {
                    await _client.EmitMessageAsync(_roomId, $"TEST_REQUEST_WITH_CONFIG|{configJson}|{code}", ct);
                }
                else
                {
                    await _client.EmitMessageAsync(_roomId, "TEST_REQUEST:" + code, ct);
                }
            }
            else
            {
                await _client.EmitMessageAsync(_roomId, "[CodeReviewer] Code needs changes. Sending back to Writer...", ct);
                var fixTask = $"Fix the code based on this review:\n{review}\n\nOriginal Code:\n{code}";
                
                if (configJson != null)
                {
                    await _client.EmitMessageAsync(_roomId, $"TASK_WITH_CONFIG|{configJson}|{fixTask}", ct);
                }
                else
                {
                    await _client.EmitMessageAsync(_roomId, $"TASK: {fixTask}", ct);
                }
            }
            
            _logger.LogInformation("CodeReviewer completed review");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in CodeReviewer");
            await _client.EmitMessageAsync(_roomId, $"[CodeReviewer] Error: {ex.Message}", ct);
        }
    }
}
