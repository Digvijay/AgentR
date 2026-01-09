using StackExchange.Redis;
using AgentR.Shared;

namespace AgentR.Server.Governance;

/// <summary>
/// Redis-backed budget service using atomic increments and decrements.
/// Ensures FinOps safety even across multi-node server deployments.
/// </summary>
public class RedisBudgetService : IBudgetService
{
    private readonly IDatabase _db;
    
    // Initial grant for new rooms (FinOps "Starter Pack")
    private const long InitialGrant = 1000; 

    public RedisBudgetService(IConnectionMultiplexer redis)
    {
        _db = redis.GetDatabase();
    }

    private string GetKey(string roomId) => $"agentr:wallet:{roomId}";

    public async Task<bool> HasFundsAsync(string roomId, long cost, CancellationToken ct = default)
    {
        var balance = await GetBalanceAsync(roomId, ct);
        
        // Auto-provision a starter pack if the room is new
        if (balance == 0)
        {
            var key = GetKey(roomId);
            if (!await _db.KeyExistsAsync(key))
            {
                await TopUpAsync(roomId, InitialGrant, ct);
                return InitialGrant >= cost;
            }
        }

        return balance >= cost;
    }

    public async Task DeductFundsAsync(string roomId, long cost, CancellationToken ct = default)
    {
        var key = GetKey(roomId);
        await _db.HashDecrementAsync(key, "balance", cost, flags: CommandFlags.None);
    }

    public async Task TopUpAsync(string roomId, long amount, CancellationToken ct = default)
    {
        var key = GetKey(roomId);
        await _db.HashIncrementAsync(key, "balance", amount, flags: CommandFlags.None);
    }

    public async Task<long> GetBalanceAsync(string roomId, CancellationToken ct = default)
    {
        var key = GetKey(roomId);
        var val = await _db.HashGetAsync(key, "balance", flags: CommandFlags.None);
        return val.HasValue ? (long)val : 0;
    }
}
