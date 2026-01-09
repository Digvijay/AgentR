using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using AgentR.Shared;
using System.Text.Json;
using System.Collections.Concurrent;

namespace AgentR.Client;

public class AgentClient
{
    private readonly HubConnection _connection;
    public string AgentId { get; }
    
    private readonly ConcurrentDictionary<string, TaskCompletionSource<string>> _pendingToolRequests = new();
    private CancellationTokenSource? _turnCts;

    // The API Surface
    public event Func<CancellationToken, Task>? OnTurnGranted;
    public event Action<string>? OnLog;
    public event Func<string, Task>? OnAssignment;
    public event Action<List<RoomInfo>>? OnDiscoveryResults;
    public event Action<AgentFrame>? OnSignalReceived;
    public event Func<string, string, CancellationToken, Task<string>>? OnToolRequest;

    public AgentClient(string hubUrl, string agentId, string apiKey, HttpMessageHandler? handler = null)
    {
        AgentId = agentId;
        var builder = new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                // Send the key/token with every connection attempt
                options.AccessTokenProvider = () => Task.FromResult<string?>(apiKey);
                if (handler != null)
                {
                    options.HttpMessageHandlerFactory = _ => handler;
                }
            })
            .WithAutomaticReconnect()
            .AddJsonProtocol();

        _connection = builder.Build();

        _connection.On<AgentFrame>(nameof(IAgentClient.ReceiveSignal), HandleSignal);
        
        // Listen for direct tool invocations (Agent-as-a-Tool)
        _connection.On<ToolRequestFrame>("InvokeTool", HandleIncomingToolRequestAsync);
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (_connection.State == HubConnectionState.Disconnected)
        {
            await _connection.StartAsync(ct);
        }
    }
    public async Task JoinRoomAsync(string roomId, string[]? tags = null, CancellationToken ct = default) => await _connection.InvokeAsync("JoinRoom", roomId, tags, ct);
    public async Task RequestTurnAsync(string roomId, CancellationToken ct = default) => await _connection.InvokeAsync("RequestTurn", roomId, ct);
    public async Task ReleaseTurnAsync(string roomId, CancellationToken ct = default) => await _connection.InvokeAsync("ReleaseTurn", roomId, ct);
    public async Task AssignAgentAsync(string targetAgentId, string newRoomId, CancellationToken ct = default) => await _connection.InvokeAsync("AssignAgent", targetAgentId, newRoomId, ct);
    public async Task LeaveRoomAsync(string roomId, CancellationToken ct = default) => await _connection.InvokeAsync("LeaveRoom", roomId, ct);
    public async Task RequestAgentAsync(string agentType, CancellationToken ct = default) => await _connection.InvokeAsync("RequestAgent", agentType, ct);
    public async Task<long> GetBalanceAsync(string roomId, CancellationToken ct = default) => await _connection.InvokeAsync<long>("GetRoomBalance", roomId, ct);
    public async Task TopUpAsync(string roomId, long amount, CancellationToken ct = default) => await _connection.InvokeAsync("TopUpRoom", roomId, amount, ct);

    public async Task DisconnectAsync(CancellationToken ct = default) => await _connection.StopAsync(ct);
    
    public async Task ListRoomsAsync(string[]? tags = null, CancellationToken ct = default)
    {
        var rooms = await _connection.InvokeAsync<List<RoomInfo>>("ListRooms", tags, ct);
        OnDiscoveryResults?.Invoke(rooms);
    }

    public async Task EmitThinkingAsync(string roomId, string content, CancellationToken ct = default) => 
        await EmitFrameAsync(new ThinkingFrame { SenderId = AgentId, RoomId = roomId, Content = content }, ct);

    public async Task EmitMessageAsync(string roomId, string content, CancellationToken ct = default) => 
        await EmitFrameAsync(new MessageFrame { SenderId = AgentId, RoomId = roomId, Content = content }, ct);

    private async Task EmitFrameAsync(AgentFrame frame, CancellationToken ct)
    {
        if (_turnCts?.IsCancellationRequested == true) return;

        try 
        {
            await _connection.InvokeAsync("Broadcast", frame, ct);
        }
        catch (HubException ex) when (ex.Message.Contains("402") || ex.Message.Contains("Safety"))
        {
            OnLog?.Invoke($"🙊 BLOCKED: {ex.Message}");
            _turnCts?.Cancel(); 
        }
        catch (Exception ex)
        {
             OnLog?.Invoke($"⚠️ FAILED to emit frame: {ex.Message}");
        }
    }

    public async Task<string> FindOrStartRoomAsync(string fallbackRoomId, string[]? tags = null, CancellationToken ct = default)
    {
        OnLog?.Invoke($"🔍 Searching for room with tags: {(tags != null ? string.Join(", ", tags) : "none")}...");
        
        var rooms = await _connection.InvokeAsync<List<RoomInfo>>("ListRooms", tags, ct);
        
        if (rooms != null && rooms.Count > 0)
        {
            var bestRoom = rooms.OrderByDescending(r => r.AgentCount).First();
            OnLog?.Invoke($"📍 Found existing room: {bestRoom.RoomId} ({bestRoom.AgentCount} agents)");
            await JoinRoomAsync(bestRoom.RoomId, tags, ct);
            return bestRoom.RoomId;
        }

        OnLog?.Invoke($"🆕 No matching room found. Creating: {fallbackRoomId}");
        await JoinRoomAsync(fallbackRoomId, tags, ct);
        return fallbackRoomId;
    }

    private void HandleSignal(AgentFrame frame)
    {
        switch (frame)
        {
            case TurnGrantedFrame:
                // Start a new task to execute the turn so we don't block the SignalR message processing loop
                _ = ExecuteTurn(frame.RoomId); 
                break;

            case InterruptFrame i:
                OnLog?.Invoke($"⚡ INTERRUPT RECEIVED: {i.Reason}");
                _turnCts?.Cancel(); // <--- The Magic Line
                break;

            case ThinkingFrame t:
                OnLog?.Invoke($"[Peer {t.SenderId}] Thinking: {t.Content}");
                break;

            case AssignmentFrame a:
                OnLog?.Invoke($"📍 ASSIGNMENT: Moving to {a.NewRoomId}");
                _ = HandleAssignment(a.NewRoomId);
                break;

            case ToolResponseFrame tr:
                if (_pendingToolRequests.TryRemove(tr.RequestId, out var tcs))
                {
                    if (tr.Success) tcs.TrySetResult(tr.ResultJson);
                    else tcs.TrySetException(new Exception(tr.ResultJson));
                }
                break;
        }

        OnSignalReceived?.Invoke(frame);
    }

    private async Task HandleAssignment(string nextRoomId)
    {
        if (OnAssignment != null)
        {
            await OnAssignment.Invoke(nextRoomId);
        }
        else
        {
            // Default behavior: Just join the room
            await JoinRoomAsync(nextRoomId);
        }
    }

    private async Task ExecuteTurn(string roomId)
    {
        _turnCts = new CancellationTokenSource();
        try
        {
            if (OnTurnGranted != null)
            {
                await OnTurnGranted.Invoke(_turnCts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            OnLog?.Invoke("🛑 Turn cancelled locally.");
        }
        finally
        {
            try
            {
                if (_turnCts != null)
                {
                    _turnCts.Dispose();
                    _turnCts = null;
                    // Auto-Release: Ensure we don't hold the lock if the logic completes or fails
                    // We check _connection.State to avoid throwing if already disconnected
                    if (_connection.State == HubConnectionState.Connected)
                    {
                        await ReleaseTurnAsync(roomId);
                    }
                }
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"⚠️ Error releasing turn: {ex.Message}");
            }
        }
    }

    public async Task<string> CallToolAsync(string roomId, string server, string tool, object args, CancellationToken ct = default)
    {
        var requestId = Guid.NewGuid().ToString();
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        _pendingToolRequests[requestId] = tcs;

        // linked token for 30s timeout
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(30));

        try
        {
            await _connection.InvokeAsync("InvokeTool", new ToolRequestFrame
            {
                SenderId = AgentId,
                RoomId = roomId,
                RequestId = requestId,
                ServerName = server,
                ToolName = tool,
                ArgumentsJson = JsonSerializer.Serialize(args)
            }, cts.Token);

            return await tcs.Task.WaitAsync(cts.Token);
        }
        finally
        {
            _pendingToolRequests.TryRemove(requestId, out _);
        }
    }

    private async Task HandleIncomingToolRequestAsync(ToolRequestFrame request)
    {
        if (OnToolRequest == null) return;

        bool success;
        string resultJson;

        try
        {
            // Execute the tool logic provided by the consumer
            resultJson = await OnToolRequest.Invoke(request.ToolName, request.ArgumentsJson, CancellationToken.None);
            success = true;
        }
        catch (Exception ex)
        {
            success = false;
            resultJson = JsonSerializer.Serialize(new { error = ex.Message });
        }

        var response = new ToolResponseFrame
        {
            RequestId = request.RequestId,
            RoomId = request.RoomId,
            SenderId = AgentId,
            Success = success,
            ResultJson = resultJson
        };

        // Send the result back to the Hub (which forwards to the caller)
        await _connection.InvokeAsync("ToolResult", response);
    }
}
