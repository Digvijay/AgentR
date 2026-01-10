using AgentR.Client;
using AgentR.Shared;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

Console.WriteLine("🤖 AgentR Worker (Worker-01) Starting...");

const string SecretKey = "ThisIsMySuperSecretKeyForAgentR_MustBe32CharsLong!";
const string HubUrl = "http://localhost:5000/agentHub";

// 1. Generate Worker Token
var tokenHandler = new JwtSecurityTokenHandler();
var key = Encoding.UTF8.GetBytes(SecretKey);
var tokenDescriptor = new SecurityTokenDescriptor
{
    Subject = new ClaimsIdentity(new[] { new Claim("sub", "Worker-01") }),
    SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
};
var token = tokenHandler.CreateToken(tokenDescriptor);
var tokenString = tokenHandler.WriteToken(token);

// 2. Connect
var client = new AgentClient(HubUrl, "Worker-01", tokenString);
client.OnLog += Console.WriteLine;

// 3. Define behavior
client.OnTurnGranted += async (stopEvent) =>
{
    Console.WriteLine("🎤 My turn! Processing task...");
    for (int i = 0; i < 5; i++)
    {
        if (stopEvent.IsCancellationRequested) break;
        await client.EmitThinkingAsync("Task-PR-123", $"Processing step {i}...");
        await Task.Delay(1000);
    }
    await client.ReleaseTurnAsync("Task-PR-123");
};

// Handle dynamic assignments
client.OnAssignment += async (room) =>
{
    Console.WriteLine($"📍 Received assignment to move to: {room}");
    await client.JoinRoomAsync(room, new[] { "assigned-task" });
    await client.RequestTurnAsync(room);
};

await client.ConnectAsync();

// Join the Lobby to wait for work
await client.JoinRoomAsync("Lobby", new[] { "worker", "idle" });

Console.WriteLine("✅ Idle in Lobby. Waiting for supervisor assignment...");

// Keep alive
await Task.Delay(-1);
