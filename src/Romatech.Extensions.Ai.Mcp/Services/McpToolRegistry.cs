using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Romatech.Extensions.Ai.Mcp.Protocol;
using Romatech.Extensions.Ai.Shared.Abstractions;
using Romatech.Extensions.Ai.Shared.Models;
using Romatech.Extensions.Ai.Swagger.Discovery;

namespace Romatech.Extensions.Ai.Mcp.Services;

/// <summary>
/// Registry of executable MCP tools. Tools come from <see cref="IAiToolBindingSource"/>, which keeps them in sync
/// with the application's endpoints (it rebuilds when the endpoint data source changes).
/// </summary>
public sealed class McpToolRegistry
{
    private readonly IAiToolBindingSource _bindingSource;
    private readonly IEndpointDiscoveryProvider _discoveryProvider;
    private readonly ILogger<McpToolRegistry> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="McpToolRegistry"/> class.
    /// </summary>
    public McpToolRegistry(
        IAiToolBindingSource bindingSource,
        IEndpointDiscoveryProvider discoveryProvider,
        ILogger<McpToolRegistry> logger)
    {
        _bindingSource = bindingSource;
        _discoveryProvider = discoveryProvider;
        _logger = logger;
    }

    /// <summary>
    /// Kept for compatibility: discovery is lazy and needs no explicit initialization.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var tools = await _bindingSource.GetToolBindingsAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogDebug("MCP tool registry has {ToolCount} tools", tools.Count);
    }

    /// <summary>
    /// Returns every executable tool binding.
    /// </summary>
    public Task<IReadOnlyList<AiToolBinding>> GetToolsAsync(CancellationToken cancellationToken = default)
        => _bindingSource.GetToolBindingsAsync(cancellationToken);

    /// <summary>
    /// Finds a tool by its exact name.
    /// </summary>
    public async Task<AiToolBinding?> FindToolAsync(string toolName, CancellationToken cancellationToken = default)
    {
        var tools = await _bindingSource.GetToolBindingsAsync(cancellationToken).ConfigureAwait(false);
        return tools.FirstOrDefault(t => string.Equals(t.Descriptor.ToolName, toolName, StringComparison.Ordinal));
    }

    /// <summary>
    /// Converts a binding into its MCP <c>tools/list</c> definition.
    /// </summary>
    public static McpToolDefinition ToDefinition(AiToolBinding tool) => new()
    {
        Name = tool.Descriptor.ToolName,
        Description = tool.Descriptor.Description,
        InputSchema = JsonNode.Parse(tool.Descriptor.InputSchema ?? """{"type":"object","properties":{}}"""),
        Annotations = BuildAnnotations(tool.HttpMethod),
    };

    /// <summary>
    /// Conservative hints derived from HTTP semantics. Clients must treat them as untrusted hints; nothing is
    /// claimed as non-destructive unless the method guarantees it.
    /// </summary>
    private static JsonObject BuildAnnotations(string httpMethod)
    {
        var annotations = new JsonObject { ["openWorldHint"] = false };
        switch (httpMethod)
        {
            case "GET":
            case "HEAD":
            case "OPTIONS":
                annotations["readOnlyHint"] = true;
                break;
            case "PUT":
                annotations["readOnlyHint"] = false;
                annotations["idempotentHint"] = true;
                break;
            case "DELETE":
                annotations["readOnlyHint"] = false;
                annotations["destructiveHint"] = true;
                annotations["idempotentHint"] = true;
                break;
            default:
                annotations["readOnlyHint"] = false;
                break;
        }
        return annotations;
    }

    /// <summary>
    /// Returns all endpoints (including read-only) for RAG indexing.
    /// </summary>
    public Task<IReadOnlyList<AiEndpointDescriptor>> GetAllEndpointsAsync(CancellationToken cancellationToken = default)
        => _discoveryProvider.DiscoverEndpointsAsync(cancellationToken);
}
