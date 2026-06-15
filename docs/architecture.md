# Architecture

## High-Level Flow

```
ASP.NET Application
       ↓
Swagger/OpenAPI Discovery
       ↓
AI Metadata Layer (Attributes + Resolution)
       ↓
┌─────────────┬─────────────┐
│  MCP Layer  │  RAG Layer  │
└─────────────┴─────────────┘
       ↓
AI Consumers
```

## Package Responsibilities

### Romatech.Extensions.Ai (Entry Point)
- Provides `services.UseMcp()` and `services.UseRag()` extension methods
- Wires up all internal dependencies
- The only package end-users install

### Romatech.Extensions.Ai.Shared
- Abstractions: `IEmbeddingProvider`, `IEndpointDiscoveryProvider`
- Models: `AiEndpointDescriptor`, `AiExposureLevel`
- Zero dependencies on ASP.NET internals

### Romatech.Extensions.Ai.Metadata
- All AI attributes (`[AiTool]`, `[AiHidden]`, etc.)
- `MetadataResolver` for reflection-based attribute resolution
- Minimal API extension methods (`.AiTool()`, `.AiHidden()`)

### Romatech.Extensions.Ai.Swagger
- `SwaggerEndpointDiscoveryProvider` reads OpenAPI spec
- `OpenApiSchemaConverter` transforms OpenAPI → MCP JSON Schema
- ETag and in-memory caching for Swagger documents

### Romatech.Extensions.Ai.Mcp
- `McpMiddleware` handles HTTP routing to MCP endpoint
- `McpRequestHandler` processes JSON-RPC requests
- `McpToolRegistry` manages discovered tools
- `McpToolExecutor` calls underlying endpoints
- `SlidingWindowRateLimiter` for per-tool rate limiting

### Romatech.Extensions.Ai.Rag
- `LocalEmbeddingProvider` for zero-dependency embeddings
- `SemanticIndexer` builds and searches the vector index
- `RagSearchService` orchestrates index lifecycle

## Key Design Decisions

1. **Convention over configuration**: No manual registration needed
2. **Async-first**: All I/O operations are async
3. **Thread-safe**: Concurrent dictionaries and lock-free reads
4. **Lazy initialization**: Indexes built on first request, not at startup
5. **Pluggable providers**: `IEmbeddingProvider` can be swapped for OpenAI, Ollama, etc.
