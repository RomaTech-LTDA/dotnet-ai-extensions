namespace Romatech.Extensions.Ai.Mcp.Configuration;

/// <summary>
/// Configuration options for the MCP layer.
/// </summary>
public sealed class McpOptions
{
    /// <summary>
    /// The route for the MCP endpoint. Default: /mcp
    /// </summary>
    public string Route { get; set; } = "/mcp";

    /// <summary>
    /// Whether to enable rate limiting on MCP tool calls. Default: true
    /// </summary>
    public bool EnableRateLimiting { get; set; } = true;

    /// <summary>
    /// Global rate limit (requests per minute) if no per-tool limit is set. Default: 60
    /// </summary>
    public int GlobalRateLimitPerMinute { get; set; } = 60;

    /// <summary>
    /// Server name reported in MCP initialize response.
    /// </summary>
    public string ServerName { get; set; } = "Romatech.Extensions.Ai";

    /// <summary>
    /// Server version reported in MCP initialize response.
    /// </summary>
    public string ServerVersion { get; set; } = "1.0.0";

    /// <summary>
    /// Whether to require authentication for MCP calls. Default: false (inherits host auth).
    /// </summary>
    public bool RequireAuthentication { get; set; } = false;

    /// <summary>
    /// Base URL for internal HTTP calls when executing tools.
    /// Default: http://localhost:5000 (auto-detected from Kestrel if possible).
    /// WARNING: The executor calls back into the same application via HTTP.
    /// Under heavy load this may cause thread pool starvation.
    /// Consider increasing ThreadPool.SetMinThreads() for high-traffic apps.
    /// </summary>
    public string BaseUrl { get; set; } = "http://localhost:5000";
}
