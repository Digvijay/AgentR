using AgentR.Client;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;
using System.Text;
#if (UseSemanticKernel)
using Microsoft.SemanticKernel;
#endif

Console.WriteLine("🤖 Starting AGENT_NAME_PLACEHOLDER...");

// --- AUTHENTICATION ---
// Generate a self-signed JWT token for testing
// TODO: In production, obtain this from your identity provider
var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("ThisIsMySuperSecretKeyForAgentR_MustBe32CharsLong!"));
var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);
var tokenDescriptor = new SecurityTokenDescriptor
{
    Subject = new ClaimsIdentity(new[] { new Claim("sub", "AGENT_NAME_PLACEHOLDER") }),
    Expires = DateTime.UtcNow.AddHours(1),
    SigningCredentials = credentials
};
var tokenHandler = new JwtSecurityTokenHandler();
var token = tokenHandler.CreateToken(tokenDescriptor);
var accessToken = tokenHandler.WriteToken(token);

Console.WriteLine($"🔑 Generated Auth Token: {accessToken[..20]}...");

#if (UseSemanticKernel)
// --- SEMANTIC KERNEL SETUP ---
// AgentR is agnostic to the LLM provider. Choose the one that fits your needs:

var kernelBuilder = Kernel.CreateBuilder();

// OPTION 1: Local LLM with Ollama (Default for local dev)
kernelBuilder.AddOpenAIChatCompletion(
    modelId: "llama3.2:latest",
    apiKey: "not-needed-for-ollama",
    httpClient: new HttpClient { BaseAddress = new Uri("http://localhost:11434/v1/") });

/*
// OPTION 2: Official OpenAI
kernelBuilder.AddOpenAIChatCompletion(
    modelId: "gpt-4o",
    apiKey: "your-openai-api-key");

// OPTION 3: Azure OpenAI
kernelBuilder.AddAzureOpenAIChatCompletion(
    deploymentName: "your-deployment-name",
    endpoint: "https://your-resource.openai.azure.com/",
    apiKey: "your-azure-api-key");
*/

var kernel = kernelBuilder.Build();

Console.WriteLine("✅ Semantic Kernel configured with your chosen LLM");
#endif

// --- AGENTR CLIENT ---
var client = new AgentClient("HUB_URL_PLACEHOLDER", "AGENT_NAME_PLACEHOLDER", accessToken);

// Define your agent's behavior
client.OnTurnGranted += async (stopEvent) =>
{
    Console.WriteLine("🎤 Turn granted! Starting work...");
    
    // TODO: Implement your agent logic here
    // Example: Call your LLM, process data, emit thinking frames, etc.
    
#if (UseSemanticKernel)
    var prompt = "Tell me a short joke about AI agents.";
    await foreach (var chunk in kernel.InvokePromptStreamingAsync(prompt))
    {
        if (stopEvent.IsCancellationRequested) 
        {
            Console.WriteLine("\n⚡ Interrupted!");
            break;
        }
        
        var text = chunk.ToString();
        if (!string.IsNullOrEmpty(text))
        {
            await client.EmitThinkingAsync("DemoRoom", text);
            Console.Write(text);
        }
    }
    Console.WriteLine();
#else
    for (int i = 0; i < 10; i++)
    {
        if (stopEvent.IsCancellationRequested)
        {
            Console.WriteLine("\n⚡ Interrupted!");
            break;
        }
        
        var message = $"Processing step {i + 1}/10";
        await client.EmitThinkingAsync("DemoRoom", message);
        Console.WriteLine(message);
        await Task.Delay(500);
    }
#endif
    
    Console.WriteLine("✅ Work complete!");
};

// Connect to the hub
await client.ConnectAsync();
await client.JoinRoomAsync("DemoRoom");

Console.WriteLine("✅ Connected to AgentR Hub");
Console.WriteLine("Press [Enter] to request turn, or Ctrl+C to exit");

// Main loop
while (true)
{
    Console.ReadLine();
    await client.RequestTurnAsync("DemoRoom");
}
