using AgentR.Client;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
#if (UseSemanticKernel)
using Microsoft.SemanticKernel;
#endif

// 🏗️ Enterprise-Grade Worker Setup
var builder = Host.CreateApplicationBuilder(args);

// 1. Configure Services and Logging
builder.Services.AddLogging(logging => logging.AddConsole());

// 2. Generate Development Token (Replace with Identity Provider in Prod)
var accessToken = GenerateDevToken("AGENT_NAME_PLACEHOLDER");
builder.Services.AddSingleton(new AgentClient("HUB_URL_PLACEHOLDER", "AGENT_NAME_PLACEHOLDER", accessToken));

#if (UseSemanticKernel)
// 3. Configure Semantic Kernel (Brain)
var kernelBuilder = Kernel.CreateBuilder();
// Option: Ollama (Local)
kernelBuilder.AddOpenAIChatCompletion(
    modelId: "llama3.2:latest",
    apiKey: "ollama",
    httpClient: new HttpClient { BaseAddress = new Uri("http://localhost:11434/v1/") });
builder.Services.AddSingleton(kernelBuilder.Build());
#endif

var host = builder.Build();
var logger = host.Services.GetRequiredService<ILogger<Program>>();
var client = host.Services.GetRequiredService<AgentClient>();

// --- AGENT LOGIC ---
client.OnTurnGranted += async (stopEvent) =>
{
    logger.LogInformation("🎤 Turn granted! Starting inference loop...");

#if (UseSemanticKernel)
    var kernel = host.Services.GetRequiredService<Kernel>();
    var prompt = "Tell me a short joke about distributed systems.";
    
    try 
    {
        await foreach (var chunk in kernel.InvokePromptStreamingAsync(prompt, cancellationToken: stopEvent))
        {
            if (!string.IsNullOrEmpty(chunk.ToString()))
            {
                await client.EmitThinkingAsync("DemoRoom", chunk.ToString());
                Console.Write(chunk);
            }
        }
    }
    catch (OperationCanceledException)
    {
        logger.LogWarning("⚡ Turn interrupted by server override.");
    }
#else
    // Simulation Loop
    for (int i = 0; i < 5; i++)
    {
        if (stopEvent.IsCancellationRequested) break;

        var msg = $"Processing step {i+1}/5...";
        await client.EmitThinkingAsync("DemoRoom", msg);
        logger.LogInformation(msg);
        
        await Task.Delay(500, stopEvent); // ✅ Propagate CancellationToken
    }
#endif
    logger.LogInformation("✅ Turn complete. Releasing lock.");
};

// --- STARTUP ---
try 
{
    await client.ConnectAsync();
    await client.JoinRoomAsync("DemoRoom");
    
    logger.LogInformation("🚀 Agent connected. Press [Enter] to request a turn.");
    
    // Simple interactive loop
    while (true)
    {
        Console.ReadLine();
        logger.LogInformation("Creating turn request...");
        await client.RequestTurnAsync("DemoRoom");
    }
}
catch (Exception ex)
{
    logger.LogCritical(ex, "Fatal error on startup");
}

// --- HELPERS ---
static string GenerateDevToken(string agentName)
{
    var key = Encoding.UTF8.GetBytes("ThisIsMySuperSecretKeyForAgentR_MustBe32CharsLong!");
    var tokenDescriptor = new SecurityTokenDescriptor
    {
        Subject = new ClaimsIdentity(new[] { new Claim("sub", agentName) }),
        Expires = DateTime.UtcNow.AddHours(1),
        SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256)
    };
    return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityTokenHandler().CreateToken(tokenDescriptor));
}
