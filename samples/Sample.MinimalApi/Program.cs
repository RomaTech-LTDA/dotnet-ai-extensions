using Romatech.Extensions.Ai.Extensions;
using Romatech.Extensions.Ai.Metadata.Extensions;

var builder = WebApplication.CreateBuilder(args);

// The user configures their own OpenAPI generator (Swashbuckle, Scalar, MS OpenApi, etc.)
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// AI enablement — zero boilerplate
// The lib auto-detects the OpenAPI endpoint from registered services
builder.Services.UseMcp(options =>
{
    options.Route = "/mcp";
    options.EnableRateLimiting = true;
    // Optional: manually specify endpoint if auto-detection doesn't work
    // options.OpenApiEndpoint = "/swagger/v1/swagger.json";
});
builder.Services.UseRag();

var app = builder.Build();

// The user manages their own API documentation UI
app.UseSwagger();
app.UseSwaggerUI();

// AI middleware — only adds MCP/RAG capabilities, no UI rendering
app.UseMcp();
app.UseRag();

// Minimal API endpoints with AI metadata
app.MapGet("/api/products", () =>
{
    return Results.Ok(new[] { new { Id = 1, Name = "Widget", Price = 9.99 } });
})
.AiDescription("Lists all available products")
.AiCategory("Products");

app.MapPost("/api/products", (CreateProductRequest request) =>
{
    return Results.Ok(new { Id = 2, request.Name, request.Price });
})
.AiTool("create_product")
.AiDescription("Creates a new product in the catalog")
.AiCategory("Products")
.AiRateLimit(20);

app.MapDelete("/api/products/{id}", (int id) =>
{
    return Results.NoContent();
})
.AiHidden();

app.MapGet("/api/health", () => Results.Ok(new { Status = "Healthy" }))
    .AiHidden();

app.Run();

record CreateProductRequest(string Name, decimal Price);
