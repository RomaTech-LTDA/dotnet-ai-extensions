using Romatech.Extensions.Ai.Extensions;
using Romatech.Extensions.Ai.Metadata.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// AI enablement — zero boilerplate
builder.Services.UseMcp(options =>
{
    options.Route = "/mcp";
    options.EnableRateLimiting = true;
});
builder.Services.UseRag();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();
app.UseMcp();

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
