using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Romatech.Extensions.Ai.Mcp.Protocol;
using Romatech.Extensions.Ai.Shared.Abstractions;
using Romatech.Extensions.Ai.Shared.Models;

namespace Romatech.Extensions.Ai.Mcp.Services;

/// <summary>
/// Registry that manages discovered MCP tools and handles tool listing.
/// </summary>
public sealed class McpToolRegistry
{
    private readonly IEndpointDiscoveryProvider _discoveryProvider;
    private readonly ILogger<McpToolRegistry> _logger;
    private readonly ConcurrentDictionary<string, AiEndpointDescriptor> _tools = new();
    private volatile bool _initialized;

    public McpToolRegistry(
        IEndpointDiscoveryProvider discoveryProvider,
        ILogger<McpToolRegistry> logger)
    {
        _discoveryProvider = discoveryProvider;
        _logger = logger;
    }

    /// <summary>
    /// Initializes the tool registry by discovering endpoints.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized) return;

        var endpoints = await _discoveryProvider.DiscoverEndpointsAsync(cancellationToken);

        foreach (var endpoint in endpoints.Where(e => e.ExposureLevel == AiExposureLevel.Executable))
        {
            _tools.TryAdd(endpoint.ToolName, endpoint);
        }

        _initialized = true;
        _logger.LogInformation("MCP Tool Registry initialized with {ToolCount} tools", _tools.Count);
    }

    /// <summary>
    /// Returns all registered tool definitions for the tools/list response.
    /// </summary>
    public IReadOnlyList<McpToolDefinition> GetToolDefinitions()
    {
        return _tools.Values.Select(endpoint => new McpToolDefinition
        {
            Name = endpoint.ToolName,
            Description = endpoint.Description,
            InputSchema = endpoint.InputSchema is not null
                ? JsonSerializer.Deserialize<object>(endpoint.InputSchema)
                : null
        }).ToList();
    }

    /// <summary>
    /// Tries to get an endpoint descriptor for a given tool name.
    /// </summary>
    public bool TryGetTool(string toolName, out AiEndpointDescriptor? descriptor)
    {
        return _tools.TryGetValue(toolName, out descriptor);
    }

    /// <summary>
    /// Returns all endpoints (including read-only) for RAG indexing.
    /// </summary>
    public async Task<IReadOnlyList<AiEndpointDescriptor>> GetAllEndpointsAsync(CancellationToken cancellationToken = default)
    {
        return await _discoveryProvider.DiscoverEndpointsAsync(cancellationToken);
    }
}
