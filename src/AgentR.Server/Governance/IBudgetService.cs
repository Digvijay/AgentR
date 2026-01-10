namespace AgentR.Server.Governance;

/// <summary>
/// Providies high-performance, atomic budget management for agent rooms.
/// </summary>
public interface IBudgetService
{
    /// <summary>
    /// Checks if a room has sufficient funds for a specific cost.
    /// </summary>
    Task<bool> HasFundsAsync(string roomId, long amount = 1, CancellationToken ct = default);

    /// <summary>
    /// Atomicly deducts a specific amount from the room wallet.
    /// </summary>
    Task DeductFundsAsync(string roomId, long cost, CancellationToken ct = default);

    /// <summary>
    /// Increments the room balance.
    /// </summary>
    Task TopUpAsync(string roomId, long amount, CancellationToken ct = default);

    /// <summary>
    /// Gets or sets whether budget enforcement is active.
    /// </summary>
    bool IsEnforcementEnabled { get; set; }

    /// <summary>
    /// Retrieves the current balance for a room.
    /// </summary>
    Task<long> GetBalanceAsync(string roomId, CancellationToken ct = default);
}
