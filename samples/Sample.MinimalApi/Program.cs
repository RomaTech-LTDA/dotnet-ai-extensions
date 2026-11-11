using Microsoft.AspNetCore.Authentication.Cookies;
using Romatech.Extensions.Ai.Extensions;
using Romatech.Extensions.Ai.Metadata.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Your existing authentication: MCP callers use it too, and every tool runs as the calling user.
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie();
builder.Services.AddAuthorization();

// Any OpenAPI generator (Swashbuckle, Microsoft.AspNetCore.OpenApi, NSwag, Scalar) can coexist; none is required.

builder.Services.UseMcp(options =>
{
    // Secure defaults: authentication required, cross-site Origins rejected, per-client rate limiting.
    options.ServerName = "Products API";
    // options.AllowedOrigins.Add("https://backoffice.example.com");
});
builder.Services.UseRag();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/api/products", () => Results.Ok(new[] { new { Id = 1, Name = "Widget", Price = 9.99 } }))
    .AiDescription("Lists all available products")
    .AiCategory("Products");

app.MapPost("/api/products", (CreateProductRequest request) => Results.Ok(new { Id = 2, request.Name, request.Price }))
    .RequireAuthorization()
    .AiTool("create_product")
    .AiDescription("Creates a new product in the catalog")
    .AiCategory("Products")
    .AiRateLimit(20);

app.MapDelete("/api/products/{id:int}", (int id) => Results.NoContent())
    .RequireAuthorization()
    .AiHidden();

app.MapGet("/api/health", () => Results.Ok(new { Status = "Healthy" }))
    .AiHidden();

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

// Routed MCP endpoint. Authentication is required by McpOptions.RequireAuthentication (default true), which answers
// 401 to MCP clients; conventions such as RequireCors(...) or RequireRateLimiting(...) can be chained here.
app.MapMcp("/mcp");

app.Run();

record CreateProductRequest(string Name, decimal Price);
