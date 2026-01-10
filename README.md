# AgentR 🧠⚡🛡️

![Build Status](https://img.shields.io/badge/build-passing-brightgreen)
![Version](https://img.shields.io/badge/version-v1.0.0-blue)
![License](https://img.shields.io/badge/license-MIT-green)

**The Enterprise-Grade Real-time Protocol for Distributed AI Agents.**

AgentR is the "Nervous System" for multi-agent orchestration. It solves the hard distributed systems problems—concurrency, state synchronization, and remote interruptibility—so you can focus on building intelligent agents.

---

> **See also**: [Executive Summary](./docs/EXECUTIVE_SUMMARY.md) | [Technical Deep Dive](./docs/TECHNICAL_SUMMARY.md)

## 🌟 Why AgentR?

Most AI frameworks (Semantic Kernel, AutoGen) focus on the **Brain** (LLM reasoning). AgentR focuses on the **Network**.

| Feature | Description | Status |
| :--- | :--- | :--- |
| **Distributed Concurrency** | "Talking Stick" mutex backed by **Redis**. Reactive `FloorOpenFrame` signaling for sub-millisecond turn handoffs. | ✅ v1.0.0 |
| **Unique Identity** | **Phantom Agent Prevention**. Accurate `agentCount` via Redis Ref-counting and Set cardinality. | ✅ v1.0.0 |
| **Remote Interruptibility** | Stop runaway LLM loops instantly across the network via `CancellationToken` propagation. | ✅ v1.0.0 |
| **High Frequency** | Binary **MessagePack** protocol for 70% smaller payloads and sub-millisecond latency. | ✅ v1.0.0 |
| **Project Templates** | Scaffold enterprise-ready agents in seconds with `dotnet new agentr-worker`. | ✅ v1.0.0 |
| **Python Interop** | First-class support for Python agents via the reference `agentr` package. | ✅ v1.0.0 |
| **Enterprise Security** | JWT-based authentication and `[Authorize]` gateways for zero-trust orchestration. | ✅ v1.0.0 |
| **Governance** | "HR Department" filters to block PII, profanity, or sensitive topics at the network level. | ✅ v1.0.0 |
| **FinOps** | **Budget Controller**. Token-based quota management to prevent runaway AI costs. | ✅ v1.0.0 |
| **Flight Recorder** | **Audit & Compliance** engine. Immutable logs with cold storage (Azure Blob/S3). | ✅ v1.0.0 |
| **Cloud Native** | Built-in **.NET Aspire** orchestration for a unified local and cloud dev experience. | ✅ v1.0.0 |

---

## 🏗️ Architecture

AgentR decouples the **Hub** (State & Governance) from the **Workers** (Intelligence). This allows you to scale your agents independently of your orchestration logic.

```mermaid
sequenceDiagram
    participant User as User (UI)
    participant Hub as AgentHub (Redis)
    participant Agent as Worker (Ollama/OpenAI)

    User->>Hub: JoinRoom("WarRoom")
    Agent->>Hub: JoinRoom("WarRoom") + JWT Token
    
    Agent->>Hub: RequestTurn()
    Hub->>Hub: Acquire Distributed Lock (Redis)
    Hub-->>Agent: TurnGranted
    
    rect rgb(240, 248, 255)
    note right of Agent: Inference Loop (Semantic Kernel)
    loop Streaming Thoughts (MessagePack)
        Agent->>Hub: Frame(Type=Thinking)
        Hub--xHub: Content Safety Filter (Check)
        Hub--xHub: Flight Recorder (Log)
        Hub->>User: Frame(Type=Thinking)
    end
    end

    User->>Hub: Interrupt() ("Stop!")
    Hub->>Hub: Revoke Lock
    Hub-->>Agent: Frame(Type=Interrupt)
    
    note right of Agent: CancellationToken Triggered
    Agent-->>Hub: TurnReleased
```

---

## 🚀 Getting Started

Join the AgentR network in under 60 seconds.

### 1. Install & Scaffold
```bash
# Install the official template
dotnet new install ./templates/AgentR.Worker

# Scaffold a new agent 
dotnet new agentr-worker -n MyAgent

# Scaffold an ADVANCED agent (FinOps, MCP, KEDA)
dotnet new agentr-worker-advanced -n MyProAgent
```

### 🛡️ Reliability & Safety
AgentR now includes **Zombie Agent Protection**:
*   **Auto-Release SDK**: Both .NET and Python clients automatically release turns if your code crashes.
*   **Server WatchDog**: The Hub forcibly revokes locks after 60s (configurable via `AgentR:LeaseTimeoutSeconds`) if an agent hangs.

### 🔐 Authentication
AgentR supports **JWT-based authentication** for production deployments:

**For Local Development (Default):**
```json
{
  "AgentR": {
    "DisableAuth": true  // No authentication required
  }
}
```

**For Production:**
```json
{
  "AgentR": {
    "DisableAuth": false  // Enable JWT authentication
  }
}
```

When authentication is enabled, clients must provide a valid JWT token. See the [Authentication Guide](docs/AUTHENTICATION.md) for details on token generation and configuration.

### 2. Choose Your "Brain" (LLM-Agnostic)
AgentR is a transport layer. It doesn't care which LLM you use. The `agentr-worker` template comes pre-configured with:

| Provider | Integration Method |
| :--- | :--- |
| **Local LLM** | [Ollama](https://ollama.ai) (llama3.2, mistral, etc.) |
| **OpenAI** | GPT-4o, o1-preview |
| **Azure OpenAI** | Enterprise-grade deployments |
| **Anthropic** | Claude 3.5 Sonnet |
| **Custom** | Any LLM with a C# or Python SDK |

### 3. Launch the Stack

**Option A: Headless Core (Server + Worker)**
```bash
dotnet run --project src/AgentR.AppHost/AgentR.AppHost.csproj
```

**Option B: Full Interactive Demo (UI + Lobby + Replay)**
```bash
dotnet run --project samples/AgentR.Demo/AgentR.Demo.AppHost/AgentR.Demo.AppHost.csproj
```

### ☁️ Deploy to Azure (Production)
AgentR supports one-click deployment via the **Azure Developer CLI (`azd`)**. This provisions [Azure Container Apps](https://azure.microsoft.com/en-us/products/container-apps) and Redis automatically.

1. **Install azd**: [Get Azure Developer CLI](https://learn.microsoft.com/en-us/azure/developer/azure-developer-cli/install-azd)
2. **Login**: `azd auth login`
3. **Deploy**:
   ```bash
   azd up
   ```
   This will use the `azure.yaml` configuration to provision and deploy the entire Control Plane (Server + Worker) to your Azure subscription.

   > **Note**: The project includes synthesized Bicep templates in `infra/`.
   
   [![Deploy to Azure](https://aka.ms/deploytoazurebutton)](https://portal.azure.com/#create/Microsoft.Template/uri/https%3A%2F%2Fraw.githubusercontent.com%2Fagentr-org%2Fagentr%2Fmaster%2Finfra%2Fmain.json)
   *(Requires compiling `infra/main.bicep` to `main.json` if not using `azd`)*

### 🎮 See the Demo in Action

To experience the **Lobby Pattern** and **Scale-to-Zero** logic (requires Option B):
1. **Start the Demo AppHost**.
2. Go to the **"Agent Lobby"** in the sidebar.
3. Click **"Summon DotNet Agent"** and watch the logs in your terminal!

---

## 📂 Project Ecosystem

### Core Platform (Production Ready)
*   **`src/AgentR.Server`**: The Headless Hub. Contains all synchronization logic (Redis Locks), MCP Gateway, and Governance enforcement. **No UI.**
*   **`src/AgentR.Client`**: High-performance .NET SDK for building agents.
*   **`src/AgentR.Python`**: Native Python interop bridge.

### Samples & Templates (Reference Implementation)
*   **`samples/AgentR.Demo`**: A comprehensive Aspire application demonstrating:
    *   **Mission Control UI**: Dashboard for monitoring rooms and budgets.
    *   **Lobby UI**: Visualizer for the KEDA Scale-to-Zero pattern.
    *   **Session Replay**: UI for the "Flight Recorder" audit logs.
*   **`templates/`**: `dotnet new` scaffolds for creating your own agents.

**Note**: Features like "Mission Control" and "Agent Lobby" are reference UIs provided in the Demo project. The Core Server provides the *APIs* (SignalR Hub blocks) that enable these UIs, but does not ship with a frontend.

*   **`docs/FLIGHT_RECORDER.md`**: Specs for the Auditing and Session Replay system.
*   **`docs/KEDA_AUTOSCALING.md`**: Guide for Scale-to-Zero and the Lobby Pattern.
*   **`docs/BUDGET_CONTROLLER.md`**: FinOps specs for Token-based Quota management.
*   **`docs/MCP_GATEWAY.md`**: Architecture for the secure Model Context Protocol gateway.

## 📘 Engineering Standards

We enforce strict coding standards to guarantee performance and stability:

*   **[ASP.NET Core Standards](./docs/DOTNET_STANDARDS.md)**: Zero-allocation, Async/Await best practices (`ValueTask`, `CancellationToken`).
*   **[Python SDK Standards](./docs/PYTHON_STANDARDS.md)**: Native AsyncIO, Strong Typing with `TypedDict`, and Awaitable Tool calls.

---

## 🛡️ Governance & Auditing

AgentR includes an enterprise-grade auditing engine designed for strictly regulated industries:

- 💸 **FinOps Budgeting**: "Toll Booth" pricing model for OpenAI/Anthropic costs.
- 🔌 **Dynamic MCP Gateway**: Connect to any external MCP server via standard `mcp.json` configuration.
- ⚡ **KEDA Scale-to-Zero**: Integrated with Kubernetes for event-driven agent scaling.
- **Immutable Log Trails**: Every frame (Thinking, Messaging, Turns) is captured and hashed.
- **Resilient Flight Recorder**: "Immortal" background service that survives crashes and buffers data during storage outages.
- **Session Replay**: Watch conversation "tape" millisecond-by-millisecond to debug agent hallucinations.

**💡 TIP**
Check out the [Flight Recorder Documentation](./docs/FLIGHT_RECORDER.md) for technical deep-dives into the storage abstraction.

---

## 🚀 KEDA Autoscaling (Lobby Pattern)

AgentR supports event-driven autoscaling to zero for cost-efficient agent management:
1. **Lobby Queue**: The Hub pushes "tickets" to Redis when an agent type is requested.
2. **KEDA Scaler**: A `ScaledObject` monitors the Redis list length.
3. **Just-in-Time Provisioning**: Pods spin up to handle tickets and scale back to zero when idle.

**💡 TIP**
See `k8s/agentr-keda.yaml` for a reference implementation.

---

## 👩‍💻 Code Example: The Network-Aware Agent

Integrating AgentR with your agentic loop is as simple as passing a `CancellationToken`.

```csharp
client.OnTurnGranted += async (stopEvent) =>
{
    // 1. Call your Favorite LLM with the Network Signal
    var response = kernel.InvokePromptStreamingAsync("Plan the mission...", { 
        cancellationToken: stopEvent 
    });

    await foreach (var chunk in response)
    {
        // 2. Stream "Thoughts" via MessagePack instantly
        await client.EmitThinkingAsync("OpsRoom", chunk.Text);
    }
};
```

---

## 📦 Installation

AgentR is available on NuGet:

```bash
dotnet add package AgentR.Client
```

---

We are strictly typed. We are high performance. We are distributed.
Pull Requests welcome.

## 🧪 Quality & Validation

**📝 Validation Strategy:** 
- **Control Plane**: 32+ integration tests (xUnit/Testcontainers) validate locking, interrupts, and **Unique Identity** invariants.
- **Data Plane**: Cross-platform E2E tests validate MessagePack binary serialization between C# and Python.
- **Resiliency**: `WatchdogTests.cs` and `ResiliencyTests.cs` ensure zero-leak operation during hard disconnects.
- **Governance**: `SafetyTests.cs` and `BudgetTests.cs` verify network-level censorship and atomic billing.

---

## 📄 License

MIT

![AgentR Platform Banner](./docs/images/banner_technical.png)
