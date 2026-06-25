using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Romatech.Extensions.Ai.Mcp.Configuration;
using Romatech.Extensions.Ai.Mcp.Protocol;
using Romatech.Extensions.Ai.Mcp.Security;
using Romatech.Extensions.Ai.Rag.Services;
using Romatech.Extensions.Ai.Shared.Models;

namespace Romatech.Extensions.Ai.Mcp.Services;

/// <summary>
/// Handles incoming MCP JSON-RPC requests and routes them to the appropriate handler.
/// Supports: initialize, ping, notifications/initialized, tools/list, tools/call,
/// resources/list, resources/read, completions/complete, prompts/list, prompts/get.
/// </summary>
public sealed class McpRequestHandler
{
    private readonly McpToolRegistry _toolRegistry;
    private readonly McpToolExecutor _toolExecutor;
    private readonly McpOptions _options;
    private readonly IRateLimiter _rateLimiter;
    private readonly ILogger<McpRequestHandler> _logger;
    private RagSearchService? _ragSearch;

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
    /// Attaches the RAG search service to enable the built-in rag_search tool.
    /// </summary>
    public void AttachRagSearch(RagSearchService ragSearch)
    {
        _ragSearch = ragSearch;
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
            "notifications/initialized" => McpResponse.Success(request.Id, new { }),
            "ping" => McpResponse.Success(request.Id, new { }),
            "tools/list" => HandleToolsList(request),
            "tools/call" => await HandleToolsCallAsync(request, cancellationToken),
            "completions/complete" => HandleCompletions(request),
            "prompts/list" => HandlePromptsList(request),
            "prompts/get" => HandlePromptsGet(request),
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
                tools = new { listChanged = true },
                completions = new { },
                prompts = new { listChanged = false }
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
        var tools = _toolRegistry.GetToolDefinitions().ToList();

        // Add built-in rag_search tool if RAG is available
        if (_ragSearch is not null)
        {
            tools.Add(new McpToolDefinition
            {
                Name = "rag_search",
                Description = "Searches API documentation semantically. Returns relevant endpoints ranked by similarity.",
                InputSchema = JsonSerializer.Deserialize<object>(
                    """{"type":"object","properties":{"query":{"type":"string","description":"Natural language search query"}},"required":["query"]}""")
            });
        }

        return McpResponse.Success(request.Id, new { tools });
    }

    private async Task<McpResponse> HandleToolsCallAsync(McpRequest request, CancellationToken cancellationToken)
    {
        var toolName = request.Params?.Name;
        if (string.IsNullOrWhiteSpace(toolName))
        {
            return McpResponse.Failure(request.Id, -32602, "Missing tool name");
        }

        // Built-in rag_search
        if (toolName == "rag_search" && _ragSearch is not null)
        {
            return await HandleRagSearchAsync(request, cancellationToken);
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

    private async Task<McpResponse> HandleRagSearchAsync(McpRequest request, CancellationToken cancellationToken)
    {
        var query = request.Params?.Arguments?.GetValueOrDefault("query")?.ToString();
        if (string.IsNullOrWhiteSpace(query))
        {
            return McpResponse.Failure(request.Id, -32602, "Missing required parameter: query");
        }

        try
        {
            var results = await _ragSearch!.SearchAsync(query, cancellationToken);
            var formatted = results.Select(r => new
            {
                toolName = r.Document.ToolName,
                method = r.Document.HttpMethod,
                route = r.Document.Route,
                category = r.Document.Category,
                score = Math.Round(r.Score * 100) / 100
            });

            return McpResponse.Success(request.Id, new
            {
                content = new[]
                {
                    new { type = "text", text = JsonSerializer.Serialize(formatted) }
                }
            });
        }
        catch (Exception ex)
        {
            return McpResponse.Failure(request.Id, -32000, $"RAG search failed: {ex.Message}");
        }
    }

    private McpResponse HandleCompletions(McpRequest request)
    {
        // Provide tool name completions
        var tools = _toolRegistry.GetToolDefinitions();
        var values = tools.Select(t => t.Name).Take(10).ToArray();
        return McpResponse.Success(request.Id, new { completion = new { values } });
    }

    private McpResponse HandlePromptsList(McpRequest request)
    {
        var prompts = new[]
        {
            new { name = "list_available_tools", description = "Describes all available API tools and their capabilities" },
            new { name = "search_api", description = "Searches the API documentation for relevant endpoints" }
        };
        return McpResponse.Success(request.Id, new { prompts });
    }

    private McpResponse HandlePromptsGet(McpRequest request)
    {
        var promptName = request.Params?.Name;

        if (promptName == "list_available_tools")
        {
            var tools = _toolRegistry.GetToolDefinitions();
            var description = string.Join("\n", tools.Select(t => $"- **{t.Name}**: {t.Description ?? "No description"}"));

            return McpResponse.Success(request.Id, new
            {
                messages = new[]
                {
                    new { role = "user", content = new { type = "text", text = $"Here are the available API tools:\n\n{description}" } }
                }
            });
        }

        if (promptName == "search_api")
        {
            return McpResponse.Success(request.Id, new
            {
                messages = new[]
                {
                    new { role = "user", content = new { type = "text", text = "Search the API documentation. Use the rag_search tool." } }
                }
            });
        }

        return McpResponse.Failure(request.Id, -32602, $"Unknown prompt: {promptName}");
    }
}
