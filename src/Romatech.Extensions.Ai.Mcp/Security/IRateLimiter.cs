namespace Romatech.Extensions.Ai.Mcp.Security;

/// <summary>
/// Contract for rate limiting MCP tool calls.
/// </summary>
public interface IRateLimiter
{
    /// <summary>
    /// Attempts to acquire a rate limit token for the given tool.
    /// </summary>
    /// <param name="toolName">The tool name to rate-limit.</param>
    /// <param name="maxRequestsPerMinute">Maximum requests allowed per minute.</param>
    /// <returns>True if the request is allowed; false if rate limited.</returns>
    bool TryAcquire(string toolName, int maxRequestsPerMinute);
}
