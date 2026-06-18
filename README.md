# Romatech.Extensions.Ai

[![NuGet](https://img.shields.io/nuget/v/Romatech.Extensions.Ai)](https://www.nuget.org/packages/Romatech.Extensions.Ai)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://github.com/RomaTech-LTDA/dotnet-ai-extensions/blob/main/LICENSE)

A plug-and-play AI enablement framework for ASP.NET Core applications. Transforms existing APIs into MCP-compatible tool providers, AI-readable semantic documentation, and RAG-enabled knowledge sources — without architectural rewrites.

> **Node.js version?** See [@romatech/ai-extensions](https://github.com/RomaTech-LTDA/ai-extensions) for the equivalent framework in Node.js.

## Installation

```bash
dotnet add package Romatech.Extensions.Ai
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddSwaggerGen();

// One line to enable MCP + RAG
builder.Services.UseMcp();
builder.Services.UseRag();

var app = builder.Build();
app.UseSwagger();
app.UseMcp();
app.MapControllers();
app.Run();
```

That's it. Your APIs are now AI-consumable.

## How It Works

The framework automatically:
1. Discovers endpoints via Swagger/OpenAPI
2. Reads AI metadata attributes from your code
3. Exposes executable tools via MCP at `POST /mcp`
4. Indexes all non-hidden endpoints for RAG semantic search
5. Provides a `rag_search` MCP tool for LLM context retrieval

## AI Metadata Attributes

Control how your endpoints are exposed to AI systems:

```csharp
// Executable MCP Tool — LLMs can call this
[AiTool("create_pix_payment")]
[AiDescription("Creates a PIX payment")]
[AiCategory("Payments")]
[AiRole("finance")]
[AiRateLimit(5)]
[AiContextPriority(100)]
[HttpPost("pix")]
public IActionResult CreatePixPayment([FromBody] PixPaymentRequest request) { ... }

// Read-only — available in RAG/docs but not executable
[HttpGet]
[AiDescription("Lists all orders")]
public IActionResult GetOrders() { ... }

// Hidden — completely invisible to AI
[AiHidden]
[HttpDelete("{id}")]
public IActionResult DeleteOrder(int id) { ... }
```

### Attribute Reference

| Attribute | Purpose |
|-----------|---------|
| `[AiTool]` | Marks as executable MCP tool |
| `[AiHidden]` | Hides from all AI systems |
| `[AiDescription("...")]` | Provides AI-facing description |
| `[AiCategory("...")]` | Groups for semantic organization |
| `[AiRole("...")]` | Requires role for execution |
| `[AiRateLimit(n)]` | Max requests/minute |
| `[AiContextPriority(n)]` | RAG ranking priority |

## MCP Protocol

The framework exposes a standard MCP endpoint supporting:

```json
// Initialize
{ "method": "initialize" }

// List available tools
{ "method": "tools/list" }

// Execute a tool
{ "method": "tools/call", "params": { "name": "create_order", "arguments": {...} } }
```

## RAG Search

Automatic semantic search over your API documentation:

```json
{
  "method": "tools/call",
  "params": {
    "name": "rag_search",
    "arguments": { "query": "How does payment creation work?" }
  }
}
```

## Minimal API Support

```csharp
app.MapPost("/api/products", handler)
    .AiTool("create_product")
    .AiDescription("Creates a new product")
    .AiCategory("Products")
    .AiRateLimit(20);

app.MapDelete("/api/products/{id}", handler)
    .AiHidden();
```

## Configuration

### MCP Options

```csharp
services.UseMcp(options =>
{
    options.Route = "/mcp";
    options.EnableRateLimiting = true;
    options.GlobalRateLimitPerMinute = 60;
    options.ServerName = "My API";
});
```

### RAG Options

```csharp
services.UseRag(options =>
{
    options.IncludeXmlDocs = true;
    options.MaxSearchResults = 10;
    options.MinimumSimilarity = 0.3f;
});
```

## Exposure Rules

| State | MCP | RAG | Docs |
|-------|-----|-----|------|
| `[AiHidden]` | ❌ | ❌ | ❌ |
| `[AiTool]` | ✅ Executable | ✅ | ✅ |
| No attribute | ❌ | ✅ | ✅ |

## Security

- Inherits existing ASP.NET Core authentication automatically
- Role-based execution control via `[AiRole]`
- Per-tool rate limiting via `[AiRateLimit]`
- Global rate limiting configuration
- JWT token forwarding from MCP callers

## Architecture

```
ASP.NET Application
       ↓
Swagger/OpenAPI Discovery
       ↓
AI Metadata Layer
       ↓
MCP Layer + RAG Layer
       ↓
AI Consumers (Claude, GPT, etc.)
```

## Solution Structure

```
src/
  Romatech.Extensions.Ai/          → Main package (install this)
  Romatech.Extensions.Ai.Mcp/      → MCP server implementation
  Romatech.Extensions.Ai.Rag/      → RAG indexing and search
  Romatech.Extensions.Ai.Metadata/ → Attributes and resolution
  Romatech.Extensions.Ai.Swagger/  → OpenAPI discovery
  Romatech.Extensions.Ai.Shared/   → Abstractions and contracts
tests/
samples/
benchmarks/
```

## Supported .NET Versions

- .NET 8.0
- .NET 9.0

## Troubleshooting

**MCP endpoint returns 404:**
Ensure `app.UseMcp()` is called in the pipeline and your route matches.

**No tools discovered:**
Verify Swagger is enabled and accessible at the configured endpoint. Ensure at least one endpoint has `[AiTool]`.

**RAG returns empty results:**
Check that endpoints have descriptions and are not marked `[AiHidden]`.

## License

[MIT](https://github.com/RomaTech-LTDA/dotnet-ai-extensions/blob/main/LICENSE)
