namespace Romatech.Extensions.Ai.Swagger.Configuration;

/// <summary>
/// Options for OpenAPI document discovery.
/// </summary>
public sealed class OpenApiDiscoveryOptions
{
    /// <summary>
    /// Manual override for the OpenAPI document endpoint.
    /// When set, skips auto-detection and uses this path directly.
    /// Examples: "/swagger/v1/swagger.json", "/openapi/v1.json"
    /// Default: null (auto-detect from registered services).
    /// </summary>
    public string? ManualEndpoint { get; set; }

    /// <summary>
    /// Whether to enable in-memory caching for the discovered endpoints.
    /// </summary>
    public bool EnableCaching { get; set; } = true;

    /// <summary>
    /// Cache duration in seconds. Default: 300 (5 minutes).
    /// </summary>
    public int CacheDurationSeconds { get; set; } = 300;
}
