namespace Romatech.Extensions.Ai.Shared.Models;

/// <summary>
/// Describes a discovered API endpoint with AI-relevant metadata.
/// </summary>
public sealed class AiEndpointDescriptor
{
    /// <summary>
    /// The tool name used for MCP registration.
    /// </summary>
    public required string ToolName { get; init; }

    /// <summary>
    /// HTTP method (GET, POST, PUT, DELETE, etc.).
    /// </summary>
    public required string HttpMethod { get; init; }

    /// <summary>
    /// The route template for this endpoint.
    /// </summary>
    public required string Route { get; init; }

    /// <summary>
    /// Human-readable description for AI consumers.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// The category grouping for semantic organization.
    /// </summary>
    public string? Category { get; init; }

    /// <summary>
    /// Required role for executing this tool.
    /// </summary>
    public string? RequiredRole { get; init; }

    /// <summary>
    /// Rate limit in requests per minute.
    /// </summary>
    public int? RateLimitPerMinute { get; init; }

    /// <summary>
    /// Priority for RAG context ranking (higher = more important).
    /// </summary>
    public int ContextPriority { get; init; }

    /// <summary>
    /// The AI exposure level for this endpoint.
    /// </summary>
    public AiExposureLevel ExposureLevel { get; init; } = AiExposureLevel.ReadOnly;

    /// <summary>
    /// JSON Schema for the input parameters.
    /// </summary>
    public string? InputSchema { get; init; }

    /// <summary>
    /// JSON Schema for the output/response.
    /// </summary>
    public string? OutputSchema { get; init; }

    /// <summary>
    /// The operation ID from OpenAPI spec.
    /// </summary>
    public string? OperationId { get; init; }

    /// <summary>
    /// XML documentation summary if available.
    /// </summary>
    public string? XmlDocSummary { get; init; }
}
