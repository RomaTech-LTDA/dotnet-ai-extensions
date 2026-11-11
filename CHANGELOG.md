# Changelog

All notable changes to Romatech.Extensions.Ai will be documented in this file.

## [Unreleased]

## [4.0.0] - 2026-11-11

Security and compatibility release. **Upgrade from 3.0.0 is strongly recommended**: 3.0.0 does not work on
.NET 10 / Microsoft.AspNetCore.OpenApi and has the security issues listed below. Breaking changes are listed
in "Migrating from 3.x" in the README.

### Security
- **Path traversal (critical):** 3.0.0 built the tool URL by replacing `{param}` with the raw argument, so
  `"../../leads"` reached routes marked `[AiHidden]`. Tools are now executed in-process against the exact endpoint
  they describe; route values are set directly, never parsed from a URL, and values containing `/`, `\`, `.` or `..`
  segments (or control characters) are rejected.
- **Caller identity:** 3.0.0 called the API over HTTP without the caller's session, cookie or host. Tools now run
  as the MCP caller (same `ClaimsPrincipal`, headers, connection and host), and the endpoint's own authorization is
  evaluated exactly like `AuthorizationMiddleware` (`[Authorize]`, `RequireAuthorization`, `[AllowAnonymous]`,
  fallback policy). Route constraints (`{id:int}`) are enforced.
- **Failed calls reported as success:** non-2xx responses, authorization failures, timeouts and exceptions now return
  `isError: true` (MCP `CallToolResult`). Exception details are no longer sent to the client (trace id only).
- **Unauthenticated MCP endpoint:** `/mcp` now requires an authenticated caller by default
  (`McpOptions.RequireAuthentication = true`), with an optional `McpOptions.AuthorizationPolicy`.
- **Cross-site requests:** the `Origin` header is validated as required by the MCP Streamable HTTP transport
  (same origin or `McpOptions.AllowedOrigins`), and only `application/json` bodies are accepted, which blocks
  CSRF via "simple" requests (`text/plain`, forms).
- **Rate limiting:** limits are now per client (user id, else remote IP) and per tool instead of one bucket shared by
  every caller; `rag_search` is rate limited too; idle buckets are evicted so memory is bounded.
- `[AiRole]` is now enforced (it was documented but ignored): role-restricted tools are hidden from `tools/list`,
  `rag_search` and `prompts/get`, and refused on `tools/call`. Tools whose endpoint policies deny the caller are hidden
  from `tools/list` as well.
- Endpoints restricted with `RequireHost(...)` are never exposed as tools (fail closed).
- When the MCP endpoint is anonymous, same-origin browser requests are only trusted on loopback, `AllowedHosts` or
  host-filtering hosts, which blocks DNS rebinding.
- 401 responses carry the default scheme's challenge headers (e.g. `WWW-Authenticate`) and never a login redirect.
- Request body size (`MaxRequestBodyBytes`), response size (`MaxToolResponseBytes`) and execution time
  (`ToolTimeout`) are bounded.
- Removed the dependency on `Microsoft.OpenApi.Readers` 1.6.22 (GHSA-7pxr-59rr-hqj2, high) and on
  `Microsoft.SourceLink.GitHub` 8.0.0 (`Microsoft.Build.Tasks.Git`, GHSA-23fw-v26w-5fgq). The packages now have
  **no NuGet dependencies** (only the ASP.NET Core shared framework). NuGetAudit fails the build on any new advisory.

### Fixed
- `/mcp` returned HTTP 500 on .NET 10 (Microsoft.AspNetCore.OpenApi emits OpenAPI 3.1 via OpenAPI.NET 2.x, which the
  1.x reader could not handle). Discovery no longer reads the OpenAPI document at all: it uses ApiExplorer and
  endpoint metadata in-process, so it works with Swashbuckle, Microsoft.AspNetCore.OpenApi, NSwag, Scalar or none.
- Minimal API metadata (`.AiTool()`, `.AiHidden()`, `.AiDescription()`...) is honoured. In 3.0.0 it only worked on MVC
  when `AiMetadataOperationFilter` was registered manually.
- Every tool now has an `inputSchema` of type `object` (required by MCP); request bodies, query, route and header
  parameters are described from the real CLR types and the app's JSON options.
- `initialize` negotiates the protocol version (2025-11-25, 2025-06-18, 2025-03-26, 2024-11-05) and no longer
  advertises `tools.listChanged` or `completions`. Notifications get `202 Accepted`; batches are accepted only for
  2025-03-26 (max 20, no `initialize`); invalid `params` get `-32602`; `charset="utf-8"` is accepted.
- Inside a tool, `IHttpContextAccessor.HttpContext` returns the tool's own request; `OnStarting`, `OnCompleted` and
  `RegisterForDispose` run; route constraints and `[AllowAnonymous]` follow the middleware's order.
- `ToolTimeout` is enforced even when the endpoint ignores cancellation.
- Duplicate tool names are resolved deterministically and logged; names are restricted to `[A-Za-z0-9_.-]{1,64}`.

### Added
- `app.MapMcp()` (routed endpoint; works with `RequireAuthorization`, `RequireCors`, `RequireRateLimiting`).
- `McpOptions.AuthorizeToolCall` for checks the app performs in middleware, and `McpOptions.AllowedHosts`.
- Tool `annotations` derived from the HTTP method.
- `McpOptions`: `RequireAuthentication`, `AuthorizationPolicy`, `AllowedOrigins`, `ClientKeySelector`,
  `MaxRequestBodyBytes`, `MaxToolResponseBytes`, `ToolTimeout`, `Instructions`.
- Integration test suite running against real Kestrel on .NET 6, 7, 8, 9 and 10.

### Removed / changed (breaking)
- `AiMetadataOperationFilter`, `OpenApiEndpointDiscoveryProvider`, `OpenApiDocumentResolver`,
  `OpenApiSchemaConverter`, `OpenApiDiscoveryOptions` (replaced by `ApiExplorerEndpointDiscoveryProvider`).
- `McpOptions.BaseUrl` and `McpOptions.OpenApiEndpoint` are obsolete (CS0618) and ignored; remove assignments to them.
- Application middleware no longer runs for tool calls (3.x looped through the HTTP pipeline). Move middleware-based
  security checks into policies, filters or `McpOptions.AuthorizeToolCall`.
- `UseMcp()`/`UseRag()` no longer register `IMemoryCache` or `IHttpClientFactory`; `UseMcp()` decorates `IHttpContextAccessor`.
- `McpToolRegistry`, `McpToolExecutor` and `McpRequestHandler` APIs changed (see README "Migrating from 3.x");
  executable tools come from `IAiToolBindingSource`.
- `completions/complete` removed; the `search_api` prompt is listed only when RAG is enabled.
- `McpRequestHandler.HandleAsync` now takes the `HttpContext`; `McpRequest.Id` is a `JsonElement?` and
  `McpRequestParams.Arguments` a `Dictionary<string, JsonElement>`.
- Tools only see endpoints described by ApiExplorer (the same set an OpenAPI generator documents).

## [3.0.0] - 2026-08-22

### Changed
- OpenAPI source auto-detection (Swashbuckle / Microsoft.AspNetCore.OpenApi), UI dependencies removed, targets net6.0-net10.0.
- Standardized `index.html` landing page (GitHub Pages) with new template: header, hero, quick start, features grid, install command, footer

## [2.0.0] - 2026-06-26

### Added
- MCP: `ping`, `notifications/initialized`, `completions/complete`, `prompts/list`, `prompts/get`
- Built-in `rag_search` MCP tool (automatic when RAG is enabled)
- `AttachRagSearch()` wires RAG into MCP handler automatically
- `AiMetadataOperationFilter` injects `x-ai-*` extensions into Swagger spec
- `SwaggerEndpointDiscoveryProvider` reads `x-ai-*` for exposure levels
- `BaseUrl` configuration for internal HTTP calls
- McpMiddleware initializes tool registry on first request

### Fixed
- tools/list was returning empty because discovery always set ReadOnly
- McpToolRegistry.InitializeAsync() was never called
