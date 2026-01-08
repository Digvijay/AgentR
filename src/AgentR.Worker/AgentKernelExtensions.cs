using Microsoft.SemanticKernel;
using AgentR.Client;
using System.Text;

namespace AgentR.Worker;

public static class AgentKernelExtensions
{
    public static async Task<string> InvokeWithNetworkStreamingAsync(
        this Kernel kernel, 
        string prompt, 
        AgentClient client, 
        string roomId, 
        CancellationToken ct)
    {
        var fullResponse = new StringBuilder();

        // 1. Start the LLM Stream
        // The 'ct' here is the one linked to your AgentR Interrupt signal.
        await foreach (var update in kernel.InvokePromptStreamingAsync(prompt, cancellationToken: ct))
        {
            var text = update.ToString();
            if (!string.IsNullOrEmpty(text))
            {
                // 2. Broadcast the chunk to the network (Fire & Forget for speed)
                _ = client.EmitThinkingAsync(roomId, text);
                
                // 3. Print locally so you can see it working
                Console.Write(text);
                fullResponse.Append(text);
            }
        }

        return fullResponse.ToString();
    }
}
