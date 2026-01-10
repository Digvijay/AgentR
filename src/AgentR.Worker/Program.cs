using AgentR.Client;
using AgentR.Worker;
using Microsoft.SemanticKernel;
using AgentR.Shared;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Text;
using StackExchange.Redis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

// --- CONFIGURATION ---
var redisConnectionString = builder.Configuration.GetConnectionString("redis") ?? "localhost:6379,abortConnect=false";
var hubUrl = Environment.GetEnvironmentVariable("HubUrl") ?? "http://localhost:5000";
if (!hubUrl.EndsWith("/agentHub")) hubUrl = $"{hubUrl}/agentHub";

var agentType = Environment.GetEnvironmentVariable("AgentType") ?? "dotnet";
var agentId = $"{agentType}-Worker-{Guid.NewGuid().ToString()[..4]}";

// 1. Redis
builder.Services.AddSingleton<IConnectionMultiplexer>(ConnectionMultiplexer.Connect(redisConnectionString));

// 2. Semantic Kernel
var kernelBuilder = Kernel.CreateBuilder();
var ollamaModelId = "gpt-oss:120b-cloud";
var ollamaEndpoint = "http://localhost:11434/v1/";
kernelBuilder.AddOpenAIChatCompletion(
    modelId: ollamaModelId,
    apiKey: "ollama",
    httpClient: new HttpClient { BaseAddress = new Uri(ollamaEndpoint) }
);
builder.Services.AddSingleton(kernelBuilder.Build());

// 3. AgentClient (with simple token for demo - auth is disabled on server)
builder.Services.AddSingleton(new AgentClient(hubUrl, agentId, "demo-worker-token"));

// 4. Worker Service
builder.Services.AddHostedService<WorkerBackgroundService>();

var host = builder.Build();
host.Run();
