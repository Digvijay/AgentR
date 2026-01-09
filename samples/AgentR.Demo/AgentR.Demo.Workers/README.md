# AgentR Demo Workers - LLM Configuration

The demo workers support multiple LLM providers through environment variables.

## Configuration Options

### Option 1: Local Ollama (Default)
```bash
# No configuration needed - uses local Ollama by default
# Endpoint: http://localhost:11434/v1/
# Model: llama3.2
```

### Option 2: GPT-OSS Cloud
```bash
export LLM_ENDPOINT="http://localhost:11434/v1/"
export LLM_MODEL="gpt-oss:120b-cloud"
export LLM_API_KEY="ollama"
```

### Option 3: OpenAI
```bash
export LLM_ENDPOINT="https://api.openai.com/v1/"
export LLM_MODEL="gpt-4o"
export LLM_API_KEY="your-openai-api-key"
```

### Option 4: Azure OpenAI
```bash
export LLM_ENDPOINT="https://your-resource.openai.azure.com/"
export LLM_MODEL="gpt-4"
export LLM_API_KEY="your-azure-key"
```

## Fallback Mode

If the LLM is unavailable or not configured, workers automatically fall back to pre-written mock responses. This allows the demo to work immediately without any LLM setup.

## Running with Configuration

### Using Aspire AppHost (Recommended)
The AppHost will automatically pass the `HubUrl` to workers. For LLM configuration, set environment variables before running:

```bash
export LLM_MODEL="gpt-oss:120b-cloud"
cd samples/AgentR.Demo/AgentR.Demo.AppHost
dotnet run
```

### Running Workers Standalone
```bash
export HubUrl="https://localhost:5001/agentHub"
export LLM_ENDPOINT="http://localhost:11434/v1/"
export LLM_MODEL="gpt-oss:120b-cloud"
cd samples/AgentR.Demo/AgentR.Demo.Workers
dotnet run
```
