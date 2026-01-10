var builder = DistributedApplication.CreateBuilder(args);

// 1. Redis for Distributed Locking and Hub Backplane
var redis = builder.AddRedis("redis");

// 2. AgentR Server
var server = builder.AddProject<Projects.AgentR_Server>("agentr-server")
    .WithReference(redis)
    .WithExternalHttpEndpoints();

// 3. Code Review Demo Workers (Writer, Reviewer, Tester)
builder.AddProject<Projects.AgentR_Demo_Workers>("code-review-workers")
    .WithReference(server)
    .WithEnvironment("HubUrl", server.GetEndpoint("https"));

// 4. Demo Web Portal
builder.AddProject<Projects.AgentR_Demo_Web>("agentr-demo-portal")
    .WithExternalHttpEndpoints()
    .WithReference(server)
    .WithEnvironment("HubUrl", server.GetEndpoint("https"));

builder.Build().Run();
