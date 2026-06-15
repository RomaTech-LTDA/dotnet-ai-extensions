using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Romatech.Extensions.Ai.Mcp.Configuration;
using Romatech.Extensions.Ai.Mcp.Protocol;
using Romatech.Extensions.Ai.Mcp.Security;

namespace Romatech.Extensions.Ai.Mcp.Services;

/// <summary>
/// Handles incoming MCP JSON-RPC requests and routes them to the appropriate handler.
/// </summary>
public sealed class McpRequestHandler
{
    private readonly McpToolRegistry _toolRegistry;
    private readonly McpToolExecutor _toolExecutor;
    private readonly McpOptions _options;
    private readonly IRateLimiter _rateLimiter;
    private readonly ILogger<McpRequestHandler> _logger;

    public McpRequestHandler(
        McpToolRegistry toolRegistry,
        McpToolExecutor toolExecutor,
        IOptions<McpOptions> options,
        IRateLimiter rateLimiter,
        ILogger<McpRequestHandler> logger)
    {
        _toolRegistry = toolRegistry;
        _toolExecutor = toolExecutor;
        _options = options.Value;
        _rateLimiter = rateLimiter;
        _logger = logger;
    }

    /// <summary>
    /// Processes an MCP request and returns the response.
    /// </summary>
    public async Task<McpResponse> HandleAsync(McpRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Handling MCP request: {Method}", request.Method);

        return request.Method switch
        {
            "initialize" => HandleInitialize(request),
            "tools/list" => HandleToolsList(request),
            "tools/call" => await HandleToolsCallAsync(request, cancellationToken),
            _ => McpResponse.Failure(request.Id, -32601, $"Method not found: {request.Method}")
        };
    }

    private McpResponse HandleInitialize(McpRequest request)
    {
        return McpResponse.Success(request.Id, new
        {
            protocolVersion = "2024-11-05",
            capabilities = new
            {
                tools = new { listChanged = false }
            },
            serverInfo = new
            {
                name = _options.ServerName,
                version = _options.ServerVersion
            }
        });
    }

    private McpResponse HandleToolsList(McpRequest request)
    {
        var tools = _toolRegistry.GetToolDefinitions();
        return McpResponse.Success(request.Id, new { tools });
    }

    private async Task<McpResponse> HandleToolsCallAsync(McpRequest request, CancellationToken cancellationToken)
    {
        var toolName = request.Params?.Name;
        if (string.IsNullOrWhiteSpace(toolName))
        {
            return McpResponse.Failure(request.Id, -32602, "Missing tool name");
        }

        if (!_toolRegistry.TryGetTool(toolName, out var descriptor) || descriptor is null)
        {
            return McpResponse.Failure(request.Id, -32602, $"Unknown tool: {toolName}");
        }

        // Rate limiting check
        if (_options.EnableRateLimiting && !_rateLimiter.TryAcquire(toolName, descriptor.RateLimitPerMinute ?? _options.GlobalRateLimitPerMinute))
        {
            return McpResponse.Failure(request.Id, -32000, $"Rate limit exceeded for tool: {toolName}");
        }

        try
        {
            var result = await _toolExecutor.ExecuteAsync(descriptor, request.Params?.Arguments, cancellationToken);
            return McpResponse.Success(request.Id, new
            {
                content = new[]
                {
                    new { type = "text", text = JsonSerializer.Serialize(result) }
                }
            });
        }
        catch (UnauthorizedAccessException ex)
        {
            return McpResponse.Failure(request.Id, -32000, $"Unauthorized: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing tool {ToolName}", toolName);
            return McpResponse.Failure(request.Id, -32000, $"Tool execution failed: {ex.Message}");
        }
    }
}
