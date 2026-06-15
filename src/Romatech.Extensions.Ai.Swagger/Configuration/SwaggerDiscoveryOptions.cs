namespace Romatech.Extensions.Ai.Swagger.Configuration;

/// <summary>
/// Options for Swagger/OpenAPI discovery.
/// </summary>
public sealed class SwaggerDiscoveryOptions
{
    /// <summary>
    /// The path to the Swagger JSON endpoint. Default: /swagger/v1/swagger.json
    /// </summary>
    public string SwaggerEndpoint { get; set; } = "/swagger/v1/swagger.json";

    /// <summary>
    /// Whether to enable ETag-based caching for the Swagger document.
    /// </summary>
    public bool EnableCaching { get; set; } = true;

    /// <summary>
    /// Cache duration in seconds. Default: 300 (5 minutes).
    /// </summary>
    public int CacheDurationSeconds { get; set; } = 300;
}
