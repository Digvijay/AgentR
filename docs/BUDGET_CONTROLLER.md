# AgentR FinOps: The Budget Controller 💸🛡️

The **Budget Controller** is a core governance layer in AgentR designed to prevent "Runaway LLM Costs." It acts as a real-time toll booth, ensuring that agents only operate within their allocated financial boundaries.

## 1. The Problem: "The $15k Intern Loop"
In distributed AI systems, a simple `while(true)` loop or an agent hallucination can result in thousands of high-cost GPT-4 API calls in minutes. Without network-level enforcement, you only notice the damage when the monthly bill arrives.

## 2. The Solution: Network-Level Quotas
AgentR intercepts all agent communications at the **Hub** level and enforces a pre-paid credit model.

### 💰 Pricing Model
| Action | Cost (Credits) | Rationale |
| :--- | :--- | :--- |
| **RequestTurn** | 50 | Discourages "micro-turns" and turns-taking spam. |
| **Thinking** | 1 | Small cost for high-frequency reasoning tokens. |
| **Message** | 1 | Charge for the final, authoritative response. |
| **Interrupt** | Free | Stopping an agent is always free (incentivized safety). |
| **Status/Joins** | Free | Room management is part of the control plane. |

## 3. Architecture: How it Works
The Budget Controller is implemented as an atomic SignalR `IHubFilter` backed by Redis.

### 🔄 The "Toll Booth" Flow
1. **Wallet Initialization**: Every new Room starts with a **Starter Pack** (1000 credits).
2. **Interception**: When an agent calls `RequestTurn` or `Broadcast(Thinking)`, the `BudgetFilter` triggers.
3. **Atomic Balance Check**: The Hub performs a Redis `GET` and compares the balance against the required cost.
4. **Enforcement**:
    - **Sufficient Funds**: The Hub processes the call and executes an atomic `DECRBY` in Redis.
    - **Insufficient Funds**: The Hub throws a `402 Payment Required` HubException. This instantly triggers the Client SDK's error handling, halting the agent's logic.
5. **Top-up**: Administrators can refill a room's wallet via the REST API.

### 🛡️ Preventing Budget Drain: Reactive Turns 
To maximize budget efficiency, AgentR introduced **Reactive Turn Management**. 

**The Polling Problem (Avoided):**
In a standard turn-based system, busy agents might "poll" the server (`RequestTurn` -> `BUSY` -> `Loop`). With a cost of **50 credits per request**, an idle agent could accidentally drain a room's budget in seconds just by waiting.

**The Solution:**
Agents now wait for a `FloorOpenFrame` signal from the Hub. They only invoke `RequestTurn` when precisely notified that the lock is free, ensuring that every 50-credit expenditure has a high probability of success.

## 4. Configuration & API

### API Endpoints
- **Top-up**: `POST /wallet/{roomId}/topup?amount=5000`
- **Balance Check**: Handled internally by the Hub, but visible in state.

### Code Example: Admin Rescue
```bash
# Refill the "WarRoom" wallet with 5000 credits
curl -X POST "http://localhost:5000/wallet/WarRoom/topup?amount=5000"
```

## 5. Why it's Enterprise-Grade
- **Atomic Operations**: Uses Redis `DECR` to prevent "Double Spend" attacks where multiple agents drain a wallet simultaneously.
- **Distributed Safety**: All server instances share the same Redis counter, ensuring consistent enforcement in a cluster.
- **Non-Blocking**: The check happens in the SignalR pipeline, ensuring minimal latency overhead.

---
*Part of the [Technical Summary](https://github.com/Digvijay/AgentR/blob/main/docs/TECHNICAL_SUMMARY.md)
