using AgentR.Shared;

namespace AgentR.Demo.Web;

public class DemoState
{
    // Code Review Demo State
    public List<ActivityEntry> ActivityLog { get; set; } = new();
    public bool IsWorkflowRunning { get; set; }
    public string CurrentAgent { get; set; } = "";
    public string TaskDescription { get; set; } = "Write a function to calculate fibonacci numbers";
    public string FinalCode { get; set; } = "";
    public string TestResults { get; set; } = "";
    
    // Discovery State
    public List<RoomInfo> Rooms { get; set; } = new();
    public string? SelectedDiscoveryRoomId { get; set; }

    public event Action? OnChange;
    public void NotifyStateChanged() => OnChange?.Invoke();
}

public record ActivityEntry(DateTime Timestamp, string Agent, string Message, bool IsSimulated = false)
{
    public string Type => Agent switch
    {
        var a when a.Contains("writer") => "writer",
        var a when a.Contains("reviewer") => "reviewer",
        var a when a.Contains("tester") => "tester",
        _ => "system"
    };
}
