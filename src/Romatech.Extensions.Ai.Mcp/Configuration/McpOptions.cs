using Microsoft.AspNetCore.Http;

namespace Romatech.Extensions.Ai.Mcp.Configuration;

/// <summary>
/// Configuration options for the MCP layer.
/// </summary>
public sealed class McpOptions
{
    /// <summary>
    /// The route for the MCP endpoint when using <c>app.UseMcp()</c>. Default: /mcp
    /// </summary>
    public string Route { get; set; } = "/mcp";

    /// <summary>
    /// Whether callers of the MCP endpoint must be authenticated. Default: <c>true</c> (secure by default).
    /// Authentication is resolved with the application's own schemes (cookie, JWT, ...). Set to <c>false</c>
    /// only for endpoints that are already protected elsewhere or intentionally public.
    /// Tool calls are always authorized against each endpoint's own policies, regardless of this setting.
    /// </summary>
    public bool RequireAuthentication { get; set; } = true;

    /// <summary>
    /// Optional authorization policy name that callers of the MCP endpoint must satisfy
    /// (in addition to <see cref="RequireAuthentication"/>).
    /// </summary>
    public string? AuthorizationPolicy { get; set; }

    /// <summary>
    /// Browser origins (e.g. <c>https://app.example.com</c>) allowed to call the MCP endpoint in addition to the
    /// endpoint's own origin. Requests carrying any other <c>Origin</c> header are rejected with 403, as required by the
    /// MCP Streamable HTTP transport to prevent cross-site and DNS-rebinding attacks. Requests without an
    /// <c>Origin</c> header (non-browser MCP clients) are not affected.
    /// </summary>
    /// <remarks>
    /// This is a server-side check; it does not emit CORS headers. A browser client on another origin also needs an
    /// ASP.NET Core CORS policy for the same origin (<c>app.MapMcp().RequireCors(...)</c> or <c>app.UseCors(...)</c>).
    /// </remarks>
    public IList<string> AllowedOrigins { get; } = new List<string>();

    /// <summary>
    /// Host names (e.g. <c>app.example.com</c>, <c>*.example.com</c>) for which a same-origin browser request is trusted
    /// when the MCP endpoint does not require authentication. Without it, an anonymous MCP endpoint only trusts
    /// same-origin requests on loopback hosts or on hosts listed in <c>AllowedHosts</c> (host filtering), which blocks
    /// DNS-rebinding attacks. Authenticated MCP endpoints (the default) always accept same-origin requests.
    /// </summary>
    public IList<string> AllowedHosts { get; } = new List<string>();

    /// <summary>
    /// Optional extra authorization for every tool call, evaluated after the endpoint's own policies with the tool's
    /// request context (route values, user, headers). Return <c>false</c> to refuse with 403.
    /// Use it for checks your application performs in custom middleware (tenant, IP allow-list, API key), because
    /// application middleware does not run for in-process tool calls.
    /// </summary>
    public Func<HttpContext, Romatech.Extensions.Ai.Swagger.Discovery.AiToolBinding, ValueTask<bool>>? AuthorizeToolCall { get; set; }

    /// <summary>
    /// Whether to enable rate limiting on MCP tool calls. Default: true
    /// </summary>
    public bool EnableRateLimiting { get; set; } = true;

    /// <summary>
    /// Per-client, per-tool limit (requests per minute) when the tool has no <c>[AiRateLimit]</c>. Default: 60
    /// </summary>
    public int GlobalRateLimitPerMinute { get; set; } = 60;

    /// <summary>
    /// Computes the rate-limit partition for a caller. Default: the authenticated user's id
    /// (<c>NameIdentifier</c>, <c>sub</c> or name), otherwise the remote IP address.
    /// Behind a reverse proxy, configure <c>ForwardedHeadersOptions</c> so the remote IP is the client's.
    /// </summary>
    public Func<HttpContext, string>? ClientKeySelector { get; set; }

    /// <summary>
    /// Maximum size of an MCP request body, in bytes. Default: 1 MiB. Larger requests get 413.
    /// </summary>
    public long MaxRequestBodyBytes { get; set; } = 1024 * 1024;

    /// <summary>
    /// Maximum number of bytes of an endpoint response returned to the AI client. Default: 1 MiB.
    /// Longer responses are truncated and flagged.
    /// </summary>
    public int MaxToolResponseBytes { get; set; } = 1024 * 1024;

    /// <summary>
    /// Maximum duration of a single tool execution. Default: 30 seconds. At the deadline the endpoint's
    /// <c>RequestAborted</c> token is cancelled and the client gets an error immediately; an endpoint that ignores the
    /// token keeps running in the background until it ends (its services are disposed then).
    /// </summary>
    public TimeSpan ToolTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Server name reported in MCP initialize response.
    /// </summary>
    public string ServerName { get; set; } = "Romatech.Extensions.Ai";

    /// <summary>
    /// Server version reported in MCP initialize response.
    /// </summary>
    public string ServerVersion { get; set; } = "1.0.0";

    /// <summary>
    /// Optional instructions returned to the client in the initialize response.
    /// </summary>
    public string? Instructions { get; set; }

    /// <summary>
    /// No longer used. Tools are executed in-process, so no base URL is needed.
    /// </summary>
    [Obsolete("Tools are executed in-process since 4.0; BaseUrl has no effect and will be removed.")]
    public string BaseUrl { get; set; } = "http://localhost:5000";

    /// <summary>
    /// No longer used. Endpoints are discovered in-process from ApiExplorer, so no OpenAPI document is downloaded.
    /// </summary>
    [Obsolete("Endpoints are discovered in-process since 4.0; OpenApiEndpoint has no effect and will be removed.")]
    public string? OpenApiEndpoint { get; set; }
}
