# AgentR.Demo

![AgentR Demo Banner](./docs/images/banner_demo.png)

A premium .NET Aspire sample application demonstrating the core features of AgentR, including real-time streaming, content safety, and the **Flight Recorder** (Auditing & Compliance).

## Features

### 🕰️ Session Replay
- Navigate to **Replay**.
- Select a room (e.g., `test-room`).
- Watch the agent's reasoning process played back with accurate timing.

### 🚀 Agent Lobby (KEDA Scale-to-Zero Demo)
This is the showcase for AgentR's event-driven architecture.
- Navigate to **Agent Lobby**.
- Click **Summon DotNet Agent**.
- **The Flow**:
    1. The portal sends a request to the Hub.
    2. The Hub pushes a ticket to Redis.
    3. (In K8s) KEDA sees the ticket and spins up a worker pod.
    4. The worker joins the room and introduces itself.
- **Verification**: Watch the logs in the AppHost to see the `agentr-worker` process/container activate only after the button is clicked.
- **Visual Context**: Real-time rendering of `ThinkingFrame`, `MessageFrame`, and `InterruptFrame`.

### ☁️ Enterprise Auditing (Flight Recorder)
The demo demonstrates the "Flight Recorder" engine, which provides:
- **Asynchronous Logging**: High-speed buffering via `System.Threading.Channels`.
- **Plugable Storage**: Support for local JSONL files or **Azure Blob Storage** for cold storage.
- **Immutable Records**: Non-repudiable audit trails of all agent interactions.

### 💰 FinOps & Budgeting
The server enforces a "Toll Booth" model to prevent runaway costs:
- **Starting Budget**: Rooms start with 1000 credits.
- **Costs**: 50 credits per Turn, 1 credit per Thought.
- **Enforcement**: Watch the dashboard for "402 Payment Required" errors if a room runs out of money.
- **Learn More**: See the [Budget Controller Guide](../../docs/BUDGET_CONTROLLER.md).

## Getting Started

### Prerequisites
- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (for Redis container)
- [Ollama](https://ollama.com/) (optional, for running local LLMs)

### Running the Demo
1. Ensure Docker is running.
2. Navigate to the AppHost directory:
   ```bash
   cd samples/AgentR.Demo/AgentR.Demo.AppHost
   ```
3. Run the project:
   ```bash
   dotnet run
   ```
4. Open the **Aspire Dashboard** (URL in terminal) to see all services running.
5. Navigate to the `agentr-demo-portal` to use the Session Replay tool.

### 🎮 See it in Action 
To experience the **Lobby Pattern** and **Scale-to-Zero** logic:
1. **Start the Aspire AppHost**.
2. Go to the **"Agent Lobby"** in the sidebar of the Demo Portal.
3. Click **"Summon DotNet Agent"** and watch the logs in your terminal/dashboard!

## Architectural Overview
- **AppHost**: Orchestrates `AgentR.Server`, `AgentR.Worker`, and the Demo Portal.
- **AgentR.Server**: The central hub handling SignalR real-time communication and auditing.
- **AgentR.Worker**: A sample AI Agent (Ollama-powered) that interacts with the server.
- **AgentR.Demo.Web**: The administration portal for auditing and replay.

---
*For more details, visit the [Main AgentR Repository](https://github.com/Digvijay/AgentR).*
