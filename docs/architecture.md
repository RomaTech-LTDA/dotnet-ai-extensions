# Architecture

## High-Level Flow

```
ASP.NET Application
       ↓
ApiExplorer + endpoint metadata (in-process discovery)
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
- No NuGet dependencies (ASP.NET Core shared framework only)

### Romatech.Extensions.Ai.Metadata
- All AI attributes (`[AiTool]`, `[AiHidden]`, etc.)
- `MetadataResolver` (reflection) and `EndpointMetadataResolver` (endpoint metadata, MVC and Minimal APIs)
- Minimal API extension methods (`.AiTool()`, `.AiHidden()`)

### Romatech.Extensions.Ai.Swagger
- `ApiExplorerEndpointDiscoveryProvider` reads ApiExplorer descriptions and binds each one to its `RouteEndpoint`
  (MVC by `ActionDescriptor`, Minimal APIs by method + route structure); rebuilds when the endpoint data source changes
- `JsonSchemaGenerator` builds MCP input schemas from CLR types and the app's JSON options
- No OpenAPI document is downloaded or parsed

### Romatech.Extensions.Ai.Mcp
- `McpHttpHandler` is the HTTP front door (method, Origin, authentication, Content-Type, size, JSON-RPC shape),
  used by both `UseMcp()` (middleware) and `MapMcp()` (routed endpoint)
- `McpRequestHandler` processes JSON-RPC requests
- `McpToolRegistry` exposes the executable tools
- `McpToolExecutor` invokes the bound endpoint in-process as the caller, after evaluating the endpoint's
  authorization policies and route constraints; no HTTP loopback
- `SlidingWindowRateLimiter` for per-client, per-tool rate limiting with bounded memory

### Romatech.Extensions.Ai.Rag
- `LocalEmbeddingProvider` for zero-dependency embeddings
- `SemanticIndexer` builds and searches the vector index
- `RagSearchService` orchestrates index lifecycle

## Key Design Decisions

1. **Secure by default**: authenticated MCP callers, Origin validation, tools run as the caller under the endpoint's own policies
2. **Convention over configuration**: No manual registration needed
3. **Async-first**: All I/O operations are async
4. **Thread-safe**: Concurrent dictionaries and lock-free reads
5. **Lazy initialization**: Indexes built on first request, not at startup
6. **Pluggable providers**: `IEmbeddingProvider` can be swapped for OpenAI, Ollama, etc.
