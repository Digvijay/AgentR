var builder = DistributedApplication.CreateBuilder(args);

// 1. Spin up a Redis container automatically
var redis = builder.AddRedis("agentr-redis");

// 2. Spin up the Server, giving it the Redis connection string
// Note: We use the project name "AgentR_Server" which is derived from the project file name.
var server = builder.AddProject<Projects.AgentR_Server>("agentr-server")
                    .WithReference(redis)
                    .WithExternalHttpEndpoints(); // Allow us to hit it with cURL

// 3. Spin up the Worker (The Agent)
// We pass the Server URL so the Agent knows where to connect
builder.AddProject<Projects.AgentR_Worker>("agentr-worker")
       .WithReference(server) // Waits for server to be ready
       .WithEnvironment("OPENAI_API_KEY", "ollama") // Config injection
       .WithEnvironment("HubUrl", server.GetEndpoint("http")); // Dynamic URL discovery

builder.Build().Run();
