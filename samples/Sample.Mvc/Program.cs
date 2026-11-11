using Microsoft.AspNetCore.Authentication.Cookies;
using Romatech.Extensions.Ai.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie();
builder.Services.AddAuthorization();

builder.Services.UseMcp();
builder.Services.UseRag();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

// Middleware form: serves POST /mcp (McpOptions.Route).
app.UseMcp();

// Development-only sign-in so the sample can be exercised end to end:
//   curl -c jar -X POST http://localhost:5000/dev/login
//   curl -b jar -H "Content-Type: application/json" -X POST http://localhost:5000/mcp -d '{"jsonrpc":"2.0","id":1,"method":"tools/list"}'
if (app.Environment.IsDevelopment())
{
    app.MapPost("/dev/login", async (HttpContext ctx) =>
    {
        var identity = new System.Security.Claims.ClaimsIdentity(
            new[] { new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, "developer") },
            CookieAuthenticationDefaults.AuthenticationScheme);
        await Microsoft.AspNetCore.Authentication.AuthenticationHttpContextExtensions.SignInAsync(
            ctx, CookieAuthenticationDefaults.AuthenticationScheme, new System.Security.Claims.ClaimsPrincipal(identity));
        return Results.Ok();
    });
}


app.MapControllers();

app.Run();
