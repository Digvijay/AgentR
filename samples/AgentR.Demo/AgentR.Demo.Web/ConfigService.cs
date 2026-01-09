namespace AgentR.Demo.Web;

public class ConfigService
{
    public string LlmEndpoint { get; set; } = "";
    public string LlmKey { get; set; } = "";
    public string LlmModel { get; set; } = "gpt-4o";

    public bool HasRealConfig => !string.IsNullOrWhiteSpace(LlmEndpoint) && !string.IsNullOrWhiteSpace(LlmKey);

    public event Action? OnChanged;

    public void NotifyChanged() => OnChanged?.Invoke();
}
