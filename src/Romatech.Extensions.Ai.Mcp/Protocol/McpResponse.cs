using System.Text.Json.Serialization;

namespace Romatech.Extensions.Ai.Mcp.Protocol;

/// <summary>
/// Represents an outgoing MCP JSON-RPC response.
/// </summary>
public sealed class McpResponse
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";

    [JsonPropertyName("id")]
    public object? Id { get; set; }

    [JsonPropertyName("result")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Result { get; set; }

    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public McpError? Error { get; set; }

    public static McpResponse Success(object? id, object result) => new()
    {
        Id = id,
        Result = result
    };

    public static McpResponse Failure(object? id, int code, string message) => new()
    {
        Id = id,
        Error = new McpError { Code = code, Message = message }
    };
}

/// <summary>
/// MCP error object.
/// </summary>
public sealed class McpError
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}
