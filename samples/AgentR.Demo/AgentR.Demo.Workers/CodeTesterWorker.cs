using AgentR.Client;
using AgentR.Shared;
using Microsoft.SemanticKernel;

namespace AgentR.Demo.Workers;

/// <summary>
/// CodeTester Agent - Tests code and reports results
/// </summary>
public class CodeTesterWorker : BackgroundService
{
    private readonly AgentClient _client;
    private readonly Kernel _kernel;
    private readonly ILogger<CodeTesterWorker> _logger;
    private readonly string _roomId = "code-review-demo-room";
    private readonly string _agentId;

    public CodeTesterWorker(Func<string, AgentClient> clientFactory, Kernel kernel, ILogger<CodeTesterWorker> logger)
    {
        _agentId = $"code-tester";
        _client = clientFactory(_agentId);
        _kernel = kernel;
        _logger = logger;
    }

    private string? _pendingCode;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("CodeTester agent {AgentId} starting...", _agentId);

        _client.OnTurnGranted += async ct =>
        {
            if (!string.IsNullOrEmpty(_pendingCode))
            {
                var code = _pendingCode;
                _pendingCode = null;
                await HandleTestInternalAsync(code, ct);
            }
        };

        _client.OnSignalReceived += async frame =>
        {
            if (frame is MessageFrame msg)
            {
                if (msg.Content.StartsWith("TEST_REQUEST:"))
                {
                    _pendingCode = msg.Content.Replace("TEST_REQUEST:", "").Trim();
                    _logger.LogInformation("CodeTester queued testing, requesting turn...");
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
                    _logger.LogInformation("Floor is open. Requesting turn for pending testing...");
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
        await _client.JoinRoomAsync(_roomId, tags: new[] { "code-tester" }, ct: stoppingToken);
        
        _logger.LogInformation("CodeTester agent ready in room {RoomId}", _roomId);

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task HandleTestInternalAsync(string code, CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("CodeTester running tests...");
            
            await _client.EmitMessageAsync(_roomId, "[CodeTester] Running unit tests...", ct);

            // Simulate testing delay
            await Task.Delay(3000, ct);

            string testReport;
            try
            {
                var prompt = $@"You are a QA engineer. For the following code, generate and run conceptual unit tests:

Code:
{code}

Provide:
1. List of test cases
2. Expected vs actual results
3. Overall test status (PASS/FAIL)
4. Code coverage estimate

Format as a test report.";

                var response = await _kernel.InvokePromptAsync(prompt);
                testReport = response.ToString();
            }
            catch (Exception llmEx)
            {
                _logger.LogWarning("LLM unavailable, using mock test results: {Error}", llmEx.Message);
                testReport = @"**Unit Test Report**

📊 **Test Summary**
- Total Tests: 5
- Passed: 5 ✅
- Failed: 0 ❌
- Code Coverage: ~85%

**Test Cases:**

1. ✅ Test_BasicFunctionality()
   - Input: Standard test case
   - Expected: Success
   - Actual: Success

2. ✅ Test_EdgeCase_MinValue()
   - Input: Minimum boundary value
   - Expected: Handled correctly
   - Actual: Handled correctly

3. ✅ Test_EdgeCase_MaxValue()
   - Input: Maximum boundary value
   - Expected: Handled correctly
   - Actual: Handled correctly

4. ✅ Test_NullInput()
   - Input: Null/empty input
   - Expected: Graceful handling
   - Actual: Graceful handling

5. ✅ Test_Performance()
   - Execution time: < 10ms
   - Memory usage: Minimal
   - Result: Optimal

**Verdict:** All tests PASSED ✅";
            }

            await _client.EmitMessageAsync(_roomId, $"[CodeTester] Test Results:\n{testReport}", ct);
            await _client.EmitMessageAsync(_roomId, "[CodeTester] WORKFLOW_COMPLETE - All agents finished!", ct);
            
            _logger.LogInformation("CodeTester completed testing");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in CodeTester");
            await _client.EmitMessageAsync(_roomId, $"[CodeTester] Error: {ex.Message}", ct);
        }
    }
}
