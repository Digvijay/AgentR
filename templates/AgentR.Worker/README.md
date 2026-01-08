# AGENT_NAME_PLACEHOLDER

An AgentR worker created from the `agentr-worker` template.

## Prerequisites

- .NET 9.0 SDK
- AgentR Server running at `HUB_URL_PLACEHOLDER`
- .NET 9.0 SDK
- AgentR Server running at `HUB_URL_PLACEHOLDER`
<!--#if (UseSemanticKernel) -->
- An LLM Provider (Ollama locally, or OpenAI/Azure OpenAI via API)
<!--#endif -->

## Quick Start

```bash
# Run the agent
dotnet run

# Press Enter to request a turn
# The agent will execute its behavior and emit thinking frames
```

## Configuration

### Authentication
The template uses a hardcoded JWT secret for development. In production:
1. Replace the secret key in `Program.cs`
2. Obtain tokens from your identity provider
3. Configure token refresh logic

### Hub URL
Update `HUB_URL_PLACEHOLDER` in `Program.cs` to point to your AgentR server.

<!--#if (UseSemanticKernel) -->
### LLM Configuration
The template is configured for Ollama. To use a different provider:
1. Update the `AddOpenAIChatCompletion` call in `Program.cs`
2. Configure your API key and endpoint
3. Adjust the model ID as needed

<!--#endif -->
## Customization

The agent behavior is defined in the `OnTurnGranted` callback. Customize this to:
- Call your LLM with custom prompts
- Process external data sources
- Emit thinking frames for real-time updates
- Handle interruptions gracefully

## Learn More

- [AgentR Documentation](https://github.com/digvijay/AgentR)
- [SignalR Documentation](https://learn.microsoft.com/aspnet/core/signalr)
<!--#if (UseSemanticKernel) -->
- [Semantic Kernel Documentation](https://learn.microsoft.com/semantic-kernel)
<!--#endif -->
