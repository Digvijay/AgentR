using AgentR.Client;
using AgentR.Demo.Workers;
using Microsoft.SemanticKernel;

var builder = Host.CreateApplicationBuilder(args);

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

// AgentR Client Factory - Each worker needs its own client instance
builder.Services.AddSingleton<Func<string, AgentClient>>(sp => 
    (agentId) => new AgentClient(hubUrl, agentId, "demo-worker-token"));

// Register all three workers
builder.Services.AddHostedService<CodeWriterWorker>();
builder.Services.AddHostedService<CodeReviewerWorker>();
builder.Services.AddHostedService<CodeTesterWorker>();

var host = builder.Build();
await host.RunAsync();
