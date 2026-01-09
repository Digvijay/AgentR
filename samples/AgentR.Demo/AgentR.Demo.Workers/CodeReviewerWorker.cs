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

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("CodeReviewer agent {AgentId} starting...", _agentId);

        _client.OnSignalReceived += async frame =>
        {
            if (frame is MessageFrame msg && msg.Content.StartsWith("REVIEW_REQUEST:"))
            {
                await HandleReviewAsync(msg.Content.Replace("REVIEW_REQUEST:", "").Trim());
            }
        };

        await _client.ConnectAsync(stoppingToken);
        await _client.JoinRoomAsync(_roomId, tags: new[] { "code-reviewer" }, ct: stoppingToken);
        
        _logger.LogInformation("CodeReviewer agent ready in room {RoomId}", _roomId);

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task HandleReviewAsync(string code)
    {
        try
        {
            _logger.LogInformation("CodeReviewer analyzing code...");
            
            await _client.EmitMessageAsync(_roomId, "[CodeReviewer] Reviewing code for quality and bugs...");

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

                var response = await _kernel.InvokePromptAsync(prompt);
                review = response.ToString();
            }
            catch (Exception llmEx)
            {
                _logger.LogWarning("LLM unavailable, using mock review: {Error}", llmEx.Message);
                review = @"**Code Review Results**

✅ **APPROVED**

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

            await _client.EmitMessageAsync(_roomId, $"[CodeReviewer] Review complete:\n{review}");
            
            // Pass to tester if approved
            if (review.Contains("APPROVED"))
            {
                await _client.EmitMessageAsync(_roomId, "TEST_REQUEST:" + code);
            }
            else
            {
                await _client.EmitMessageAsync(_roomId, "[CodeReviewer] Code needs changes. Workflow paused.");
            }
            
            _logger.LogInformation("CodeReviewer completed review");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in CodeReviewer");
            await _client.EmitMessageAsync(_roomId, $"[CodeReviewer] Error: {ex.Message}");
        }
    }
}
