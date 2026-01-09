using AgentR.Client;
using AgentR.Shared;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

Console.WriteLine("🚀 AgentR Supervisor (Dispatcher) Starting...");

const string SecretKey = "ThisIsMySuperSecretKeyForAgentR_MustBe32CharsLong!";
const string HubUrl = "http://localhost:5000/agentHub";

// 1. Generate Supervisor Token
var tokenHandler = new JwtSecurityTokenHandler();
var key = Encoding.UTF8.GetBytes(SecretKey);
var tokenDescriptor = new SecurityTokenDescriptor
{
    Subject = new ClaimsIdentity(new[] { new Claim("sub", "Supervisor-01") }),
    SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
};
var token = tokenHandler.CreateToken(tokenDescriptor);
var tokenString = tokenHandler.WriteToken(token);

// 2. Connect
var client = new AgentClient(HubUrl, "Supervisor-01", tokenString);
client.OnLog += Console.WriteLine;

await client.ConnectAsync();
await client.JoinRoomAsync("Lobby", new[] { "management" });

Console.WriteLine("✅ In Lobby. Listening for idle workers...");

client.OnDiscoveryResults += (rooms) =>
{
    var lobby = rooms.FirstOrDefault(r => r.RoomId == "Lobby");
    if (lobby != null && lobby.AgentCount > 1) // Us + at least one other
    {
        Console.WriteLine($"💡 Found {lobby.AgentCount - 1} idle workers in Lobby!");
    }
};

while (true)
{
    Console.WriteLine("\n[Enter] to Assign a worker to 'Task-PR-123'");
    Console.ReadLine();

    // Authoritative Assignment
    await client.AssignAgentAsync("Worker-01", "Task-PR-123");
    
    await client.JoinRoomAsync("Task-PR-123", new[] { "active-task" });
    Console.WriteLine("📡 Direct assignment sent to 'Worker-01'!");
}
