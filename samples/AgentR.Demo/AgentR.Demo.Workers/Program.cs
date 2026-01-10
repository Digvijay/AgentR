using AgentR.Client;
using AgentR.Demo.Workers;
using Microsoft.SemanticKernel;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();


// Configuration
var hubUrl = Environment.GetEnvironmentVariable("HubUrl") ?? "https://localhost:5001";
if (!hubUrl.EndsWith("/agentHub")) hubUrl = $"{hubUrl}/agentHub";

// LLM Configuration - Supports Ollama local or GPT-OSS cloud
var llmEndpoint = Environment.GetEnvironmentVariable("LLM_ENDPOINT") ?? "http://localhost:11434/v1/";
var llmModel = Environment.GetEnvironmentVariable("LLM_MODEL") ?? "gpt-oss:120b-cloud";
var llmApiKey = Environment.GetEnvironmentVariable("LLM_API_KEY") ?? "ollama";

// Semantic Kernel with configurable LLM
var kernelBuilder = Kernel.CreateBuilder();

// Add OpenAI-compatible chat completion (works with Ollama and GPT-OSS cloud)
kernelBuilder.AddOpenAIChatCompletion(
    modelId: llmModel,
    apiKey: llmApiKey,
    httpClient: new HttpClient { BaseAddress = new Uri(llmEndpoint) }
);

builder.Services.AddSingleton(kernelBuilder.Build());

builder.Services.AddSingleton<Func<string, AgentClient>>(sp => 
    (agentId) => 
    {
        // Generate JWT token
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes("ThisIsMySuperSecretKeyForAgentR_MustBe32CharsLong!"); 
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[] { 
                new Claim(ClaimTypes.NameIdentifier, agentId), // Unique per worker
                new Claim("sub", agentId)
            }),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
        };
        var token = tokenHandler.CreateToken(tokenDescriptor);
        var jwtToken = tokenHandler.WriteToken(token);

        return new AgentClient(hubUrl, agentId, jwtToken);
    });

// Register all three workers
builder.Services.AddHostedService<CodeWriterWorker>();
builder.Services.AddHostedService<CodeReviewerWorker>();
builder.Services.AddHostedService<CodeTesterWorker>();

var host = builder.Build();
await host.RunAsync();
