using Microsoft.AspNetCore.SignalR;
using AgentR.Shared;

namespace AgentR.Server.Governance;

/// <summary>
/// Intercepts SignalR Hub invocations to enforce the "Toll Booth" model.
/// Rejects requests with HubException (402) if funds are insufficient.
/// </summary>
public class BudgetFilter : IHubFilter
{
    private readonly IBudgetService _budgetService;

    // Pricing Model: Configurable in a production scenario
    private const long CostPerTurn = 50;
    private const long CostPerToken = 1;

    public BudgetFilter(IBudgetService budgetService)
    {
        _budgetService = budgetService;
    }

    public async ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext context, Func<HubInvocationContext, ValueTask<object?>> next)
    {
        string? roomId = null;
        long cost = 0;

        // 1. Identify context and calculate estimated cost
        if (context.HubMethodName == "RequestTurn")
        {
            roomId = context.HubMethodArguments[0]?.ToString();
            cost = CostPerTurn;
        }
        else if (context.HubMethodName == "Broadcast")
        {
            // For broadcasting frames, extract the RoomId from the frame
            if (context.HubMethodArguments[0] is AgentFrame frame)
            {
                roomId = frame.RoomId;
                
                // Charge for Thinking tokens and final Message responses
                if (frame is ThinkingFrame || frame is MessageFrame)
                {
                    cost = CostPerToken;
                }
            }
        }

        // 2. Enforce logic if we have a chargeable action
        if (!string.IsNullOrEmpty(roomId) && cost > 0)
        {
            // A. Check for sufficient funds BEFORE execution
            if (!await _budgetService.HasFundsAsync(roomId, cost))
            {
                // REJECTION: This travels back to the Client SDK as a HubException.
                // We use the 402 code in the message for semantic clarity.
                throw new HubException("402 Payment Required: Room budget exceeded. Please top up your wallet.");
            }

            // B. Execute the actual Hub method
            var result = await next(context);

            // C. Post-Deduction: Charge the wallet only if the call succeeded
            await _budgetService.DeductFundsAsync(roomId, cost);

            return result;
        }

        // Free methods (JoinRoom, ListRooms, etc.) pass through directly
        return await next(context);
    }
}
