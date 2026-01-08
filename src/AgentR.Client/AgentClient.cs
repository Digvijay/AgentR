using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using AgentR.Shared;

namespace AgentR.Client;

public class AgentClient
{
    private readonly HubConnection _connection;
    public string AgentId { get; }
    
    private CancellationTokenSource? _turnCts;

    // The API Surface
    public event Func<CancellationToken, Task>? OnTurnGranted;
    public event Action<string>? OnLog;

    public AgentClient(string hubUrl, string agentId, string apiKey)
    {
        AgentId = agentId;
        _connection = new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                // Send the key/token with every connection attempt
                options.AccessTokenProvider = () => Task.FromResult<string?>(apiKey);
            })
            .WithAutomaticReconnect()
            .AddMessagePackProtocol()
            .Build();

        _connection.On<AgentFrame>(nameof(IAgentClient.ReceiveSignal), HandleSignal);
    }

    public async Task ConnectAsync() => await _connection.StartAsync();
    public async Task JoinRoomAsync(string roomId) => await _connection.InvokeAsync("JoinRoom", roomId);
    public async Task RequestTurnAsync(string roomId) => await _connection.InvokeAsync("RequestTurn", roomId);
    public async Task ReleaseTurnAsync(string roomId) => await _connection.InvokeAsync("ReleaseTurn", roomId);

    public async Task EmitThinkingAsync(string roomId, string content)
    {
        if (_turnCts?.IsCancellationRequested == true) return;

        try 
        {
            await _connection.InvokeAsync("Broadcast", new ThinkingFrame 
            { 
                SenderId = AgentId, RoomId = roomId, Content = content 
            });
        }
        catch (HubException ex) when (ex.Message.Contains("Safety Policy"))
        {
            // 1. Log locally
            OnLog?.Invoke($"🙊 CENSORED: {ex.Message}");
            
            // 2. Kill the turn locally so the agent stops embarrassing itself
            _turnCts?.Cancel(); 
        }
        catch (Exception ex)
        {
             OnLog?.Invoke($"⚠️ FAILED to emit thought: {ex.Message}");
        }
    }

    private void HandleSignal(AgentFrame frame)
    {
        switch (frame)
        {
            case TurnGrantedFrame:
                // Start a new task to execute the turn so we don't block the SignalR message processing loop
                _ = ExecuteTurn(); 
                break;

            case InterruptFrame i:
                OnLog?.Invoke($"⚡ INTERRUPT RECEIVED: {i.Reason}");
                _turnCts?.Cancel(); // <--- The Magic Line
                break;

            case ThinkingFrame t:
                OnLog?.Invoke($"[Peer {t.SenderId}] Thinking: {t.Content}");
                break;
        }
    }

    private async Task ExecuteTurn()
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
            _turnCts.Dispose();
            _turnCts = null;
        }
    }
}
