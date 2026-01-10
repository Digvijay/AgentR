using Microsoft.JSInterop;
using System.Text.Json;

namespace AgentR.Demo.Web;

public class ConfigService
{
    private readonly IJSRuntime _js;

    public ConfigService(IJSRuntime js)
    {
        _js = js;
    }

    private string _llmEndpoint = "";
    public string LlmEndpoint
    {
        get => _llmEndpoint;
        set { _llmEndpoint = value; Save(); }
    }

    private string _llmKey = "";
    public string LlmKey
    {
        get => _llmKey;
        set { _llmKey = value; Save(); }
    }

    private string _llmModel = "gpt-oss:120b-cloud";
    public string LlmModel
    {
        get => _llmModel;
        set { _llmModel = value; Save(); }
    }

    public bool HasRealConfig => !string.IsNullOrWhiteSpace(LlmEndpoint) && !string.IsNullOrWhiteSpace(LlmKey);

    public event Action? OnChanged;

    public void NotifyChanged() => OnChanged?.Invoke();

    private async void Save()
    {
        try
        {
            // Simple validation to prevent saving empty default state over existing if accidental
            var json = JsonSerializer.Serialize(new { LlmEndpoint, LlmKey, LlmModel });
            await _js.InvokeVoidAsync("localStorage.setItem", "agentr_llm_config", json);
            NotifyChanged();
        }
        catch 
        { 
            // Ignored during prerendering or if JS unavailable
        }
    }

    public async Task LoadAsync()
    {
        try
        {
            var json = await _js.InvokeAsync<string>("localStorage.getItem", "agentr_llm_config");
            if (!string.IsNullOrEmpty(json))
            {
                var data = JsonSerializer.Deserialize<ConfigData>(json);
                if (data != null)
                {
                    _llmEndpoint = data.LlmEndpoint ?? "";
                    _llmKey = data.LlmKey ?? "";
                    _llmModel = data.LlmModel ?? "gpt-oss:120b-cloud";
                    NotifyChanged();
                }
            }
        }
        catch 
        {
            // Ignored
        }
    }

    private class ConfigData 
    { 
        public string? LlmEndpoint { get; set; } 
        public string? LlmKey { get; set; } 
        public string? LlmModel { get; set; } 
    }
}
