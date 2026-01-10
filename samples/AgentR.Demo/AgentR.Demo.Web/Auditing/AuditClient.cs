using System.Net.Http.Json;
using AgentR.Shared;

namespace AgentR.Demo.Web.Auditing;

public class AuditClient
{
    private readonly HttpClient _httpClient;

    public AuditClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<List<AuditRecord>> GetReplayAsync(string roomId)
    {
        return await _httpClient.GetFromJsonAsync<List<AuditRecord>>($"/replay/{roomId}") ?? new();
    }
}
