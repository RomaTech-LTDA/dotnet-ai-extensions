namespace Romatech.Extensions.Ai.Metadata.Attributes;

/// <summary>
/// Specifies the rate limit in requests per minute for this endpoint when called via MCP.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class AiRateLimitAttribute : Attribute
{
    /// <summary>
    /// Maximum requests per minute.
    /// </summary>
    public int RequestsPerMinute { get; }

    /// <summary>
    /// Creates a new AiRateLimit attribute.
    /// </summary>
    /// <param name="requestsPerMinute">Maximum requests per minute allowed.</param>
    public AiRateLimitAttribute(int requestsPerMinute)
    {
        if (requestsPerMinute <= 0)
            throw new ArgumentOutOfRangeException(nameof(requestsPerMinute), "Rate limit must be greater than zero.");

        RequestsPerMinute = requestsPerMinute;
    }
}
