using System.Text.Json.Serialization;

namespace Romatech.Extensions.Ai.Mcp.Protocol;

/// <summary>
/// Represents a tool definition in the MCP tools/list response.
/// </summary>
public sealed class McpToolDefinition
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; init; }

    [JsonPropertyName("inputSchema")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? InputSchema { get; init; }
}
