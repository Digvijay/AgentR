using AgentR.Demo.Web;
using AgentR.Demo.Web.Components;
using AgentR.Demo.Web.Auditing;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire components.
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddOutputCache();

builder.Services.AddHttpClient<AuditClient>(client =>
    {
        var url = builder.Configuration["HubUrl"] ?? "http://agentr-server";
        client.BaseAddress = new(url);
    });

// --- AgentR Client Configuration ---
// For LOCAL DEVELOPMENT: Using simple token (auth disabled on server)
builder.Services.AddScoped<AgentR.Client.AgentClient>(sp => 
    new AgentR.Client.AgentClient($"{builder.Configuration["HubUrl"] ?? "http://agentr-server"}/agentHub", "DemoUser", "demo-token"));

// For PRODUCTION: Generate a valid JWT token
// 1. Add NuGet package: System.IdentityModel.Tokens.Jwt
// 2. Uncomment and use the following code:
/*
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

builder.Services.AddScoped<AgentR.Client.AgentClient>(sp => 
{
    var hubUrl = $"{builder.Configuration["HubUrl"] ?? "http://agentr-server"}/agentHub";
    
    // Generate JWT token
    var tokenHandler = new JwtSecurityTokenHandler();
    var key = Encoding.UTF8.GetBytes("ThisIsMySuperSecretKeyForAgentR_MustBe32CharsLong!");
    var tokenDescriptor = new SecurityTokenDescriptor
    {
        Subject = new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "DemoUser") }),
        Expires = DateTime.UtcNow.AddHours(1),
        SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
    };
    var token = tokenHandler.CreateToken(tokenDescriptor);
    var jwtToken = tokenHandler.WriteToken(token);
    
    return new AgentR.Client.AgentClient(hubUrl, "DemoUser", jwtToken);
});
*/

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseAntiforgery();

app.UseOutputCache();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapDefaultEndpoints();

app.Run();
