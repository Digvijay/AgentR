using AgentR.Client;
using AgentR.Worker;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using AgentR.Shared;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Text;

Console.Title = "AI Agent Worker (Ollama)";

// --- CONFIGURATION FOR OLLAMA ---
var ollamaModelId = "gpt-oss:120b-cloud"; // Stronger model
var ollamaEndpoint = "http://localhost:11434/v1/"; // The OpenAI-compatible endpoint (MUST end with /)
var dummyKey = "ollama"; // Key is ignored but required by SDK

// 1. Build the Brain (Semantic Kernel pointing to Localhost)
var builder = Kernel.CreateBuilder();

// We inject the standard OpenAI connector but redirect it to Ollama
builder.AddOpenAIChatCompletion(
    modelId: ollamaModelId,
    apiKey: dummyKey,
    httpClient: new HttpClient { BaseAddress = new Uri(ollamaEndpoint) }
);

var kernel = builder.Build();

// 2. Build the Nervous System (AgentR)
// Aspire injects the URL via the "HubUrl" env var (defined in AppHost)
// If running standalone, fallback to localhost.
var hubUrl = Environment.GetEnvironmentVariable("HubUrl") ?? "http://localhost:5000";
// Ensure correct path format for SignalR
if (!hubUrl.EndsWith("/agentHub")) hubUrl = $"{hubUrl}/agentHub";

// --- AUTHENTICATION ---
// Generate a self-signed token for testing
var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("ThisIsMySuperSecretKeyForAgentR_MustBe32CharsLong!"));
var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);
var tokenDescriptor = new SecurityTokenDescriptor
{
    // Minimal claims for this demo
    Subject = new ClaimsIdentity(new[] { new Claim("sub", "Ollama-Worker") }),
    Expires = DateTime.UtcNow.AddHours(1),
    SigningCredentials = credentials
};
var tokenHandler = new JwtSecurityTokenHandler();
var token = tokenHandler.CreateToken(tokenDescriptor);
var jwt = tokenHandler.WriteToken(token);

Console.WriteLine($"🔑 Generated Auth Token: {jwt[..10]}...");

var client = new AgentR.Client.AgentClient(hubUrl, "Ollama-Worker", jwt);

// --- OLLAMA HEALTH CHECK ---
using (var httpClient = new HttpClient())
{
    try 
    {
        // "qwen3:4b" might not be pulled, but /api/tags or simply the root should respond.
        // If the user uses /v1, the root usually returns "Ollama is running"
        var checkUrl = ollamaEndpoint.Replace("/v1", ""); 
        var response = await httpClient.GetAsync(checkUrl);
        if (response.IsSuccessStatusCode) 
            Console.WriteLine($"✅ Ollama found at {checkUrl}");
        else 
            Console.WriteLine($"⚠️ Ollama reachable at {checkUrl} but returned status: {response.StatusCode}");
    }
    catch (Exception ex)
    {
         Console.WriteLine($"❌ WARNING: Ollama is NOT reachable at {ollamaEndpoint}. Ensure it is running! Error: {ex.Message}");
    }
}

await client.ConnectAsync();
await client.JoinRoomAsync("DemoRoom");

Console.WriteLine($"✅ Connected to AgentR. Brain: {ollamaModelId} (Local).");

// 3. Define the Behavior
client.OnTurnGranted += async (ct) =>
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine("\n🎤 Turn Acquired. Streaming from Ollama...");
    Console.ResetColor();

    try 
    {
        // A prompt that will trigger the filter (Testing "rum", "mutiny", "kill")
        var prompt = "Write a story about a pirate mutiny involving lots of rum.";

        // THE MAGIC LINE
        var response = await kernel.InvokeWithNetworkStreamingAsync(
            prompt, 
            client, 
            "DemoRoom", 
            ct // <--- Linked to the Network Interrupt
        );

        Console.WriteLine("\n\n✅ Finished.");
        await client.ReleaseTurnAsync("DemoRoom"); 
    }
    catch (OperationCanceledException)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("\n\n🛑 NETWORK INTERRUPT RECEIVED. OLLAMA STOPPED.");
        Console.ResetColor();
    }
};

// Main Loop
while (true)
{
    Console.WriteLine("\n[Press Enter] to request turn...");
    Console.ReadLine();
    await client.RequestTurnAsync("DemoRoom");
}