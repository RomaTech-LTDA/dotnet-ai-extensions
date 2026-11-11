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
builder.Services.AddAuthentication(/* your scheme: cookie, JWT, ... */);
builder.Services.AddAuthorization();

builder.Services.UseMcp();   // MCP tools
builder.Services.UseRag();   // semantic search over your API (optional)

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapMcp();                // POST /mcp  (or app.UseMcp() as middleware)
app.Run();
```

Mark the endpoints an AI client may execute with `[AiTool]` (MVC) or `.AiTool()` (Minimal APIs).

## How It Works

1. **Discovery, in-process.** Endpoints are read from ASP.NET Core's ApiExplorer and endpoint metadata, the same model
   Swashbuckle, Microsoft.AspNetCore.OpenApi and NSwag read. No OpenAPI document is downloaded or parsed, so any
   generator (or none) works, on every .NET version from 6 to 10.
2. **Execution, in-process.** `tools/call` invokes the endpoint the tool was built from, directly. There is no HTTP
   loopback and no base URL. The call runs **as the MCP caller**: same user (`ClaimsPrincipal`), headers, host and
   connection. The endpoint's own authorization (`[Authorize]`, `RequireAuthorization(...)`, `[AllowAnonymous]`,
   fallback policy) and route constraints are evaluated exactly as `AuthorizationMiddleware` and routing would.
3. **Arguments cannot change the target.** Route values are set directly, never concatenated into a URL; values with
   `/`, `\`, `.` or `..` segments or control characters are rejected; query values are encoded.
4. **Honest results.** Non-2xx responses, authorization failures, timeouts and exceptions come back as
   `isError: true`. Exception details are never sent to the client, only a trace id.
5. **RAG.** Every non-hidden endpoint is indexed and searchable through the built-in `rag_search` tool.

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

Streamable HTTP transport, stateless, JSON responses (`POST` only; `Content-Type: application/json`).
Supported protocol revisions: 2025-11-25, 2025-06-18, 2025-03-26, 2024-11-05.
Methods: `initialize`, `ping`, `tools/list`, `tools/call`, `prompts/list`, `prompts/get`; notifications are
acknowledged with `202 Accepted`. JSON-RPC batches are accepted only for protocol revision 2025-03-26 (up to 20
messages). Tools carry conservative `annotations` derived from the HTTP method (`readOnlyHint` for GET, `destructiveHint`
for DELETE).

```json
{ "jsonrpc": "2.0", "id": 1, "method": "initialize", "params": { "protocolVersion": "2025-06-18", "capabilities": {}, "clientInfo": { "name": "my-client", "version": "1.0" } } }

{ "jsonrpc": "2.0", "id": 2, "method": "tools/list" }

{ "jsonrpc": "2.0", "id": 3, "method": "tools/call", "params": { "name": "create_order", "arguments": { "productId": "sku-1", "quantity": 2 } } }
```

A tool result carries the endpoint's response body as text and `isError: true` when the endpoint did not return 2xx:

```json
{ "jsonrpc": "2.0", "id": 3, "result": { "content": [ { "type": "text", "text": "{\"id\":42}" } ], "isError": false } }
```

## RAG Search

Automatic semantic search over your API documentation:

```json
{
  "jsonrpc": "2.0",
  "id": 4,
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
builder.Services.UseMcp(options =>
{
    options.Route = "/mcp";                        // for app.UseMcp()
    options.RequireAuthentication = true;          // default: callers must be authenticated
    options.AuthorizationPolicy = "ai-clients";    // optional extra policy for the MCP endpoint
    options.AllowedOrigins.Add("https://backoffice.example.com"); // server-side Origin allow-list (also enable CORS)
    options.AllowedHosts.Add("ai.example.com");    // only needed when RequireAuthentication = false
    options.AuthorizeToolCall = (ctx, tool) =>     // checks your app does in custom middleware
        ValueTask.FromResult(ctx.User.HasClaim("tenant", ctx.Request.Headers["X-Tenant"].ToString()));
    options.EnableRateLimiting = true;
    options.GlobalRateLimitPerMinute = 60;         // per client, per tool (unless [AiRateLimit])
    options.ClientKeySelector = ctx => ctx.User.FindFirst("tenant")?.Value ?? McpClientKey.Default(ctx);
    options.MaxRequestBodyBytes = 1024 * 1024;
    options.MaxToolResponseBytes = 1024 * 1024;    // larger responses are truncated and flagged
    options.ToolTimeout = TimeSpan.FromSeconds(30);
    options.ServerName = "My API";
});
```

### Routed endpoint

`app.MapMcp()` returns an `IEndpointConventionBuilder`, so the usual conventions apply:

```csharp
app.MapMcp().RequireCors("backoffice");
```

`AllowedOrigins` is a server-side check required by the MCP transport; it does not emit CORS headers. A browser client
on another origin needs both: the origin in `AllowedOrigins` and an ASP.NET Core CORS policy allowing `POST` with the
`content-type`, `mcp-protocol-version` and `authorization` headers (plus credentials when cookies are used).
With cookie authentication prefer `McpOptions.RequireAuthentication` / `AuthorizationPolicy` over
`MapMcp().RequireAuthorization()`: the endpoint convention lets the cookie handler answer 302 to the login page, while
the MCP options always answer 401 with the scheme's challenge headers.

### RAG Options

```csharp
builder.Services.UseRag(options =>
{
    options.MaxSearchResults = 10;
    options.MinimumSimilarity = 0.3f;
});
```

## Exposure Rules

| State | MCP | RAG | Docs |
|-------|-----|-----|------|
| `[AiHidden]` | No | No | No |
| `[AiTool]` | Executable | Yes | Yes |
| No attribute | No | Yes | Yes |

## Security

| Concern | Behaviour |
|---------|-----------|
| MCP endpoint authentication | Required by default (`RequireAuthentication`), using the app's own schemes. Optional `AuthorizationPolicy`. |
| Tool authorization | Each tool call is authorized against the endpoint's own `[Authorize]` / `RequireAuthorization` / `[AllowAnonymous]` / fallback policy, as the caller, in the same order as `AuthorizationMiddleware`. Tools the caller cannot use are hidden from `tools/list`. |
| Authorization in custom middleware | **Not applied to tool calls** (application middleware does not run in-process). Move tenant, IP allow-list, API-key or path-based checks into policies, endpoint/MVC filters, or `McpOptions.AuthorizeToolCall` before marking an endpoint `[AiTool]`. |
| Host-restricted endpoints | Endpoints with `RequireHost(...)` are never exposed as tools. |
| `[AiRole]` | Enforced. Each attribute is a requirement (AND); `"a,b"` means either role (OR). Hidden from `tools/list` and `rag_search` for other users. |
| Cross-site requests | `Origin` must be same-origin or in `AllowedOrigins`; only `application/json` bodies are accepted (blocks CSRF via simple requests). When `RequireAuthentication = false`, same-origin requests are trusted only on loopback, `AllowedHosts` or host-filtering hosts (blocks DNS rebinding). |
| Path traversal | Prevented by construction: the endpoint is fixed by the tool, route values are set directly and validated. |
| Rate limiting | Per client (user id, else remote IP; configure `ForwardedHeaders` behind a proxy) and per tool; bounded memory. |
| Resource limits | Request body, response size and execution time are capped. |
| Error disclosure | Exceptions are logged server-side; the client receives `HTTP 500` and the trace id only. |
| Dependencies | No NuGet dependencies: only the ASP.NET Core shared framework. |

Notes:
- Tools run in-process, so **application middleware is not re-run** for the tool call. Authorization declared on the
  endpoint is reproduced; authorization performed in middleware is not (see the table). Endpoint-level
  `RequireRateLimiting`, CORS, output caching and custom `IAuthorizationMiddlewareResultHandler`s do not apply to tool
  calls (the MCP endpoint itself can use them via `MapMcp()`). MVC filters and Minimal API endpoint filters do run.
- Inside a tool, `IHttpContextAccessor.HttpContext` is the tool's own request (route values, query, body, request
  services). `UseMcp()` decorates the accessor to make this work.
- `ToolTimeout` cancels the endpoint's `RequestAborted`; the client gets an error at the deadline even if the endpoint
  ignores the token (it then finishes in the background and its services are disposed afterwards).
- Endpoints with form or file parameters are not exposed as tools.

## Architecture

```
ASP.NET Application
       |
ApiExplorer + endpoint metadata (in-process)
       |
AI Metadata Layer
       |
MCP Layer + RAG Layer
       |
AI Consumers (Claude, GPT, Copilot, etc.)
```

The library is **agnostic to your OpenAPI generator and UI**. It never reads or renders the OpenAPI document. Use Swagger UI, Scalar, or any other tool you prefer.

## Solution Structure

```
src/
  Romatech.Extensions.Ai/          -> Main package (install this)
  Romatech.Extensions.Ai.Mcp/      -> MCP server implementation
  Romatech.Extensions.Ai.Rag/      -> RAG indexing and search
  Romatech.Extensions.Ai.Metadata/ -> Attributes and resolution
  Romatech.Extensions.Ai.Swagger/  -> In-process endpoint discovery + JSON Schema
  Romatech.Extensions.Ai.Shared/   -> Abstractions and contracts
tests/
samples/
benchmarks/
```

## Supported .NET Versions

- .NET 6.0
- .NET 7.0
- .NET 8.0 (LTS)
- .NET 9.0
- .NET 10.0+

## Migrating from 3.x

v4.0 fixes security issues in 3.0.0 (see CHANGELOG). Breaking changes:

1. **The MCP endpoint requires authentication by default.** Authenticate your MCP client with the app's scheme, or set
   `options.RequireAuthentication = false` deliberately (tools still enforce their own endpoint policies).
2. **Browser calls from other origins are rejected** unless the origin is in `options.AllowedOrigins`. That list is a
   server-side check and sends no CORS headers: a cross-origin browser client also needs a CORS policy for the same
   origin (`app.MapMcp().RequireCors("policy")` or `app.UseCors("policy")` before `app.UseMcp()`).
3. **Requests must be `Content-Type: application/json`.**
4. **Application middleware no longer runs for tool calls.** 3.x called tools over HTTP through the whole pipeline;
   4.0 invokes the endpoint in-process with the caller's identity. Security checks done in custom middleware (tenant,
   IP allow-list, API key, path-based roles) must move to authorization policies, endpoint/MVC filters or
   `McpOptions.AuthorizeToolCall`. Endpoint metadata (`[Authorize]`, policies, fallback policy) is enforced.
5. **Remove `c.OperationFilter<AiMetadataOperationFilter>()`** from Swashbuckle setup: discovery no longer uses the
   OpenAPI document. Delete any assignment to `McpOptions.BaseUrl` / `McpOptions.OpenApiEndpoint`: they have no effect
   and raise CS0618 (a build error under `TreatWarningsAsErrors`).
6. **Tool results** use `isError: true` for any non-2xx status, and a JSON body is passed through as text.
7. Endpoints must be visible to ApiExplorer (any endpoint your OpenAPI document shows is). `UseMcp()` registers
   `AddEndpointsApiExplorer()` for Minimal APIs. Endpoints with `RequireHost(...)` or form/file parameters are not tools.
8. `UseMcp()` / `UseRag()` no longer call `AddMemoryCache()` or `AddHttpClient()`; register them yourself if your own
   services relied on that. The named clients `RomatechAiOpenApi` and `RomatechAiMcpInternal` no longer exist.
   `UseMcp()` now decorates `IHttpContextAccessor` (see Security notes).
9. Code using the MCP plumbing directly must be updated: `McpToolRegistry.TryGetTool/GetToolDefinitions` became
   `FindToolAsync/GetToolsAsync/ToDefinition`; `McpToolExecutor.ExecuteAsync(HttpContext, AiToolBinding, ...)` returns
   `McpToolCallResult`; `McpRequestHandler.HandleAsync` takes the `HttpContext`. Resolve these types from DI.
   Executable tools come from `IAiToolBindingSource`; a custom `IEndpointDiscoveryProvider` now only affects RAG.
10. `completions/complete` was removed (nothing to complete; the spec method is `completion/complete`), and the
    `search_api` prompt is only listed when RAG is enabled.
11. `Romatech.Extensions.Ai.Shared` and `.Rag` now reference the ASP.NET Core shared framework instead of
    `Microsoft.Extensions.*` packages (all packages target ASP.NET Core apps).

## Troubleshooting

**401 from /mcp:** the caller is not authenticated with the app's default scheme. Send the same cookie or bearer token
the API accepts, or configure `RequireAuthentication`.

**403 from /mcp:** a browser `Origin` that is not allowed, or `AuthorizationPolicy` not satisfied.

**415 from /mcp:** send `Content-Type: application/json`.

**No tools listed:** mark endpoints with `[AiTool]` / `.AiTool()`; check that they are not `[AiHidden]`, that your
`[AiRole]` matches the caller, and that the endpoint appears in ApiExplorer (it is excluded with
`ExcludeFromDescription()` / `[ApiExplorerSettings(IgnoreApi = true)]`). Startup logs list how many tools were found.

**A tool returns HTTP 401/403:** the MCP caller does not satisfy that endpoint's authorization policies (or
`AuthorizeToolCall`), the same answer the endpoint's policies would give a direct call.

**403 "Origin not allowed" although the CORS preflight succeeded:** add the origin to `McpOptions.AllowedOrigins` too.

## License

[MIT](https://github.com/RomaTech-LTDA/dotnet-ai-extensions/blob/main/LICENSE)
