using AgentR.Demo.Web;
using AgentR.Demo.Web.Components;
using AgentR.Demo.Web.Auditing;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire components.
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddScoped<ConfigService>();
builder.Services.AddScoped<DemoState>();
builder.Services.AddOutputCache();

builder.Services.AddHttpClient<AuditClient>(client =>
    {
        var url = builder.Configuration["HubUrl"] ?? "http://agentr-server";
        client.BaseAddress = new(url);
    });

// --- AgentR Client Configuration ---

builder.Services.AddScoped<AgentR.Client.AgentClient>(sp => 
{
    var hubUrl = $"{builder.Configuration["HubUrl"] ?? "http://localhost:5000"}/agentHub";
    
    // Generate JWT token
    var tokenHandler = new JwtSecurityTokenHandler();
    // MATCHES THE SERVER SECRET
    var key = Encoding.UTF8.GetBytes("ThisIsMySuperSecretKeyForAgentR_MustBe32CharsLong!"); 
    var tokenDescriptor = new SecurityTokenDescriptor
    {
        Subject = new ClaimsIdentity(new[] { 
            new Claim(ClaimTypes.NameIdentifier, "DemoUser"),
            new Claim("sub", "DemoUser") // Standard identity claim
        }),
        Expires = DateTime.UtcNow.AddHours(1),
        SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
    };
    var token = tokenHandler.CreateToken(tokenDescriptor);
    var jwtToken = tokenHandler.WriteToken(token);
    
    return new AgentR.Client.AgentClient(hubUrl, "DemoUser", jwtToken);
});

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
