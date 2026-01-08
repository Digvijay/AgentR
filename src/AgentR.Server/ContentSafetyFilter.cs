using Microsoft.AspNetCore.SignalR;
using AgentR.Shared;

namespace AgentR.Server;

public class ContentSafetyFilter : IHubFilter
{
    // In production, use Azure AI Content Safety. For now, a simple list.
    private static readonly HashSet<string> _bannedWords = new(StringComparer.OrdinalIgnoreCase) 
    { 
        "mutiny", "kill", "rum" // Let's censor our Pirate for fun
    };

    public async ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext invocationContext, 
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        // We only care about the "Broadcast" method
        if (invocationContext.HubMethodName.Equals("Broadcast", StringComparison.OrdinalIgnoreCase))
        {
            // Inspect the first argument (AgentFrame)
            if (invocationContext.HubMethodArguments.Count > 0 &&
                invocationContext.HubMethodArguments[0] is AgentFrame frame)
            {
                // Check content based on frame type
                string? contentToCheck = frame switch 
                {
                    ThinkingFrame t => t.Content,
                    MessageFrame m => m.Content,
                    _ => null
                };

                if (!string.IsNullOrEmpty(contentToCheck))
                {
                    if (_bannedWords.Any(bad => contentToCheck.Contains(bad, StringComparison.OrdinalIgnoreCase)))
                    {
                        Console.WriteLine($"🛡️ BLOCKED: Agent {frame.SenderId} tried to say '{contentToCheck}'");
                        
                        // Throwing this exception stops the message from reaching other clients
                        throw new HubException("Safety Policy Violation: Profanity detected.");
                    }
                }
            }
        }

        // If safe, let it pass to the Hub
        return await next(invocationContext);
    }
}
