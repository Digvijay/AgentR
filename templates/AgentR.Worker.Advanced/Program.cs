using AgentR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = Host.CreateApplicationBuilder(args);

// 1. Observability
builder.Services.AddLogging(logging => 
{
    logging.AddConsole();
    logging.SetMinimumLevel(LogLevel.Information);
});

// 2. Identity (Simulated for Template)
var agentName = "AdvancedWorker-" + Guid.NewGuid().ToString()[..4];
var token = GenerateToken(agentName);

// 3. AgentR Client Registration (Singleton)
builder.Services.AddSingleton<AgentClient>(sp => 
    new AgentClient("http://localhost:5000/agentHub", agentName, token));

// 4. Register Hosted Service (The Agent Loop)
builder.Services.AddHostedService<Worker>();

// 5. Register Logic Components (Tools)
builder.Services.AddSingleton<SystemInfoTool>();

var host = builder.Build();
host.Run();

// --- Helper ---
static string GenerateToken(string sub)
{
    var key = Encoding.UTF8.GetBytes("ThisIsMySuperSecretKeyForAgentR_MustBe32CharsLong!");
    var desc = new SecurityTokenDescriptor
    {
        Subject = new ClaimsIdentity([new Claim("sub", sub)]),
        Expires = DateTime.UtcNow.AddHours(24),
        SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256)
    };
    return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityTokenHandler().CreateToken(desc));
}
