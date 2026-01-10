# 🔐 Authentication Guide

AgentR supports a JWT-based authentication mechanism to secure the "War Room" (Hub) in production environments.

By default, the `agentr-worker` template and the server are configured for **Development Mode** (`DisableAuth: true`), allowing anonymous connections.

## 1. Enabling Authentication

To enable authentication on the **Server**, update your `appsettings.json` (or environment variables):

```json
{
  "AgentR": {
    "DisableAuth": false
  }
}
```

When this flag is set to `false`, the Hub will reject any connection attempts that do not provide a valid Bearer token.

## 2. Token Configuration

In the current v1.0.0 implementation, the server uses a symmetric key for validation.

> [!WARNING]
> **Demo Key**: The default server implementation uses a hardcoded key:  
> `ThisIsMySuperSecretKeyForAgentR_MustBe32CharsLong!`  
>
> In a production scenario, you **MUST** replace this with a secure key stored in Azure Key Vault or AWS Secrets Manager.

### Validation Logic
The server validates the following:
*   [x] **Signature**: Must be signed with the shared secret.
*   [ ] **Issuer**: Ignored (in v1.0).
*   [ ] **Audience**: Ignored (in v1.0).
*   [ ] **Lifetime**: Ignored (in v1.0).

## 3. Connecting Clients

When authentication is enabled, your Agents (Workers) must provide the token when connecting.

### .NET Client

```csharp
var client = new AgentClientBuilder()
    .WithUrl("http://localhost:5000/agentHub")
    // Pass the JWT token here
    .WithToken("YOUR_JWT_TOKEN_HERE") 
    .Build();

await client.ConnectAsync();
```

### Python Client

```python
import asyncio
from agentr import AgentClient

async def main():
    client = AgentClient(
        url="http://localhost:5000/agentHub",
        token="YOUR_JWT_TOKEN_HERE"
    )
    await client.connect()

if __name__ == "__main__":
    asyncio.run(main())
```

## 4. SignalR & WebSockets

SignalR cannot send HTTP Headers during the initial WebSocket handshake in browsers. AgentR handles this by accepting the token via the Query String:

`ws://localhost:5000/agentHub?access_token=YOUR_JWT_TOKEN`
