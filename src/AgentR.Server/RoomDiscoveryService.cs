using StackExchange.Redis;
using AgentR.Shared;
using System.Text.Json;

namespace AgentR.Server;

public class RoomDiscoveryService
{
    private readonly IDatabase _db;

    public RoomDiscoveryService(IConnectionMultiplexer redis)
    {
        _db = redis.GetDatabase();
    }

    private static RedisKey GetRoomKey(string roomId) => $"agentr:room:{roomId}";

    public async Task RegisterRoomAsync(string roomId, string[]? tags = null, CancellationToken ct = default)
    {
        var key = GetRoomKey(roomId);
        var tagsStr = tags != null ? string.Join(",", tags) : "";
        
        await _db.HashSetAsync(key, new HashEntry[] {
            new("roomId", roomId),
            new("tags", tagsStr),
            new("lastSeen", DateTime.UtcNow.ToString("o"))
        }, flags: CommandFlags.None);

        await _db.SetAddAsync("agentr:rooms", roomId, flags: CommandFlags.None);
    }

    public async Task UpdateAgentCountAsync(string roomId, string agentId, int delta, CancellationToken ct = default)
    {
        var key = GetRoomKey(roomId);
        var agentsSetKey = $"{key}:agents";
        var connsKey = $"{key}:agent_conns";
        
        // 1. Update connection reference count for this agent
        var newConnCount = await _db.HashIncrementAsync(connsKey, agentId, delta);
        
        if (newConnCount <= 0)
        {
            // Last connection for this agent is gone
            await _db.HashDeleteAsync(connsKey, agentId);
            await _db.SetRemoveAsync(agentsSetKey, agentId);
        }
        else
        {
            // Agent has at least one active connection
            await _db.SetAddAsync(agentsSetKey, agentId);
        }

        // 2. Authoritative unique agent count is the set cardinality
        var uniqueCount = await _db.SetLengthAsync(agentsSetKey);
        await _db.HashSetAsync(key, "agentCount", (int)uniqueCount);
    }

    public async Task<List<RoomInfo>> ListRoomsAsync(string[]? tags = null, CancellationToken ct = default)
    {
        var roomIds = await _db.SetMembersAsync("agentr:rooms", flags: CommandFlags.None);
        var results = new List<RoomInfo>();

        foreach (var roomId in roomIds)
        {
            var key = GetRoomKey(roomId!);
            var entries = await _db.HashGetAllAsync(key, flags: CommandFlags.None);
            if (entries.Length == 0) continue;

            var info = new RoomInfo
            {
                RoomId = roomId!,
                Tags = entries.FirstOrDefault(e => e.Name == "tags").Value.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries),
                AgentCount = (int)entries.FirstOrDefault(e => e.Name == "agentCount").Value
            };

            if (tags == null || tags.Length == 0 || tags.Any(t => info.Tags.Contains(t)))
            {
                results.Add(info);
            }
        }

        return results;
    }
}
