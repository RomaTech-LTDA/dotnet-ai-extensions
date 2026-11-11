using System.Text.Json;
using System.Text.Json.Serialization;

namespace Romatech.Extensions.Ai.Mcp.Protocol;

/// <summary>
/// Represents an incoming MCP JSON-RPC request.
/// </summary>
public sealed class McpRequest
{
    [JsonPropertyName("jsonrpc")]
    public string? JsonRpc { get; set; }

    /// <summary>
    /// The request id (a JSON string or number), echoed back verbatim. Absent for notifications.
    /// </summary>
    [JsonPropertyName("id")]
    public JsonElement? Id { get; set; }

    [JsonPropertyName("method")]
    public string? Method { get; set; }

    [JsonPropertyName("params")]
    public McpRequestParams? Params { get; set; }

    /// <summary>
    /// True when the message has no <c>id</c> member (a JSON-RPC notification).
    /// </summary>
    [JsonIgnore]
    public bool IsNotification => Id is null || Id.Value.ValueKind == JsonValueKind.Undefined;
}

/// <summary>
/// Parameters for an MCP request.
/// </summary>
public sealed class McpRequestParams
{
    /// <summary>Tool or prompt name.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Tool or prompt arguments.</summary>
    [JsonPropertyName("arguments")]
    public Dictionary<string, JsonElement>? Arguments { get; set; }

    /// <summary>Protocol version requested by the client in <c>initialize</c>.</summary>
    [JsonPropertyName("protocolVersion")]
    public string? ProtocolVersion { get; set; }

    /// <summary>Pagination cursor.</summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; set; }

    /// <summary>Any other parameter.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}
