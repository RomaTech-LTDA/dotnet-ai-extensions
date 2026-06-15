using System.Text.Json.Serialization;

namespace Romatech.Extensions.Ai.Mcp.Protocol;

/// <summary>
/// Represents an incoming MCP JSON-RPC request.
/// </summary>
public sealed class McpRequest
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";

    [JsonPropertyName("id")]
    public object? Id { get; set; }

    [JsonPropertyName("method")]
    public string Method { get; set; } = string.Empty;

    [JsonPropertyName("params")]
    public McpRequestParams? Params { get; set; }
}

/// <summary>
/// Parameters for an MCP request.
/// </summary>
public sealed class McpRequestParams
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("arguments")]
    public Dictionary<string, object>? Arguments { get; set; }
}
