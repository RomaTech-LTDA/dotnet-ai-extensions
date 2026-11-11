using System.Text.Json.Serialization;

namespace Romatech.Extensions.Ai.Mcp.Protocol;

/// <summary>
/// Represents a tool definition in the MCP tools/list response.
/// </summary>
public sealed class McpToolDefinition
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = default!;

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; init; }

    [JsonPropertyName("inputSchema")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? InputSchema { get; init; }

    /// <summary>
    /// Tool annotations (hints): <c>readOnlyHint</c>, <c>destructiveHint</c>, <c>idempotentHint</c>, <c>openWorldHint</c>.
    /// </summary>
    [JsonPropertyName("annotations")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Annotations { get; init; }
}
