# AgentR Python SDK

The official Python client for the AgentR Real-time Transport Protocol.

## Installation
```bash
cd src/AgentR.Python
python3 -m venv venv
source venv/bin/activate
pip install .
```

## Usage
```python
from agentr import AgentRClient
import jwt

# Generate Auth Token
SECRET_KEY = "ThisIsMySuperSecretKeyForAgentR_MustBe32CharsLong!"
token = jwt.encode({"sub": "MyBot"}, SECRET_KEY, algorithm="HS256")

# Connect
client = AgentRClient("http://localhost:5000/agentHub", "MyBot", token)
client.connect()

# Define Behavior
def my_agent_logic(stop_event):
    for i in range(10):
        if stop_event.is_set():
            print("Interrupted!")
            return
        print(f"Step {i}")
        time.sleep(1)

client.on_turn_granted = my_agent_logic
client.request_turn()

## Reactive Pattern
Instead of polling, use the `on_floor_open` event to handle network congestion gracefully:

```python
pending_task = "Write a unit test"

async def handle_floor():
    global pending_task
    if pending_task:
        print("Floor is open! Claiming turn...")
        await client.request_turn()

client.on_floor_open = handle_floor
```

## Key Features
- ✅ JWT Authentication
- ✅ MessagePack Protocol
- ✅ Remote Interruption via `stop_event`
- ✅ Reactive Concurrency (Floor-is-Open Signaling)
- ✅ Flight Recorder (Auditing) Support: Python agents benefit from the Hub's automatic frame logging and compliance trails.
- ✅ KEDA / Lobby Ready: Compatible with event-driven "Scale-to-Zero" architectures when paired with the Hub's assignment logic.
- ✅ FinOps / Budget Aware: Automatically subject to the server's quota management and "Toll Booth" pricing.
