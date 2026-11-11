using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Romatech.Extensions.Ai.Mcp.Configuration;
using Romatech.Extensions.Ai.Mcp.Protocol;
using Romatech.Extensions.Ai.Mcp.Security;
using Romatech.Extensions.Ai.Rag.Services;
using Romatech.Extensions.Ai.Shared.Abstractions;

namespace Romatech.Extensions.Ai.Mcp.Services;

/// <summary>
/// Handles MCP JSON-RPC requests: initialize, ping, notifications, tools/list, tools/call,
/// prompts/list and prompts/get.
/// </summary>
public sealed class McpRequestHandler
{
    /// <summary>Protocol revisions this server implements, newest first.</summary>
    public static readonly IReadOnlyList<string> SupportedProtocolVersions = new[]
    {
        "2025-11-25", "2025-06-18", "2025-03-26", "2024-11-05",
    };

    private const string RagToolName = "rag_search";

    private readonly McpToolRegistry _toolRegistry;
    private readonly McpToolExecutor _toolExecutor;
    private readonly IRateLimiter _rateLimiter;
    private readonly IEndpointDiscoveryProvider _discoveryProvider;
    private readonly McpOptions _options;
    private readonly ILogger<McpRequestHandler> _logger;
    private RagSearchService? _ragSearch;

    /// <summary>
    /// Initializes a new instance of the <see cref="McpRequestHandler"/> class.
    /// </summary>
    public McpRequestHandler(
        McpToolRegistry toolRegistry,
        McpToolExecutor toolExecutor,
        IOptions<McpOptions> options,
        IRateLimiter rateLimiter,
        IEndpointDiscoveryProvider discoveryProvider,
        IServiceProvider services,
        ILogger<McpRequestHandler> logger)
    {
        _toolRegistry = toolRegistry;
        _toolExecutor = toolExecutor;
        _options = options.Value;
        _rateLimiter = rateLimiter;
        _discoveryProvider = discoveryProvider;
        _logger = logger;
        _ragSearch = services.GetService<RagSearchService>();
    }

    /// <summary>
    /// Attaches the RAG search service to enable the built-in rag_search tool.
    /// (It is attached automatically when <c>UseRag()</c> was called on the service collection.)
    /// </summary>
    public void AttachRagSearch(RagSearchService ragSearch)
    {
        _ragSearch = ragSearch ?? throw new ArgumentNullException(nameof(ragSearch));
    }

    /// <summary>
    /// Processes an MCP request on behalf of the HTTP caller. Returns <c>null</c> for notifications.
    /// </summary>
    public async Task<McpResponse?> HandleAsync(McpRequest request, HttpContext context, CancellationToken cancellationToken = default)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        if (context is null) throw new ArgumentNullException(nameof(context));

        _logger.LogDebug("Handling MCP request: {Method}", request.Method);

        if (request.IsNotification)
            return null; // notifications/initialized, notifications/cancelled, ...: nothing to answer

        var id = request.Id;
        return request.Method switch
        {
            "initialize" => HandleInitialize(request),
            "ping" => McpResponse.Success(id, new { }),
            "tools/list" => await HandleToolsListAsync(id, context, cancellationToken).ConfigureAwait(false),
            "tools/call" => await HandleToolsCallAsync(request, context, cancellationToken).ConfigureAwait(false),
            "prompts/list" => HandlePromptsList(id),
            "prompts/get" => await HandlePromptsGetAsync(request, context, cancellationToken).ConfigureAwait(false),
            // Legacy clients send notifications/initialized with an id.
            "notifications/initialized" => McpResponse.Success(id, new { }),
            _ => McpResponse.Failure(id, McpErrorCodes.MethodNotFound, $"Method not found: {request.Method}"),
        };
    }

    private McpResponse HandleInitialize(McpRequest request)
    {
        var requested = request.Params?.ProtocolVersion;
        var version = requested is not null && SupportedProtocolVersions.Contains(requested)
            ? requested
            : SupportedProtocolVersions[0];

        var result = new Dictionary<string, object?>
        {
            ["protocolVersion"] = version,
            ["capabilities"] = new
            {
                tools = new { listChanged = false },
                prompts = new { listChanged = false },
            },
            ["serverInfo"] = new { name = _options.ServerName, version = _options.ServerVersion },
        };
        if (!string.IsNullOrWhiteSpace(_options.Instructions))
            result["instructions"] = _options.Instructions;

        return McpResponse.Success(request.Id, result);
    }

    private async Task<McpResponse> HandleToolsListAsync(JsonElement? id, HttpContext context, CancellationToken cancellationToken)
    {
        var tools = (await GetVisibleToolsAsync(context, cancellationToken).ConfigureAwait(false))
            .Where(t => _ragSearch is null || t.Descriptor.ToolName != RagToolName)
            .Select(McpToolRegistry.ToDefinition)
            .ToList();

        if (_ragSearch is not null)
        {
            tools.Add(new McpToolDefinition
            {
                Name = RagToolName,
                Description = "Searches the API documentation semantically. Returns relevant endpoints ranked by similarity.",
                InputSchema = System.Text.Json.Nodes.JsonNode.Parse(
                    """{"type":"object","properties":{"query":{"type":"string","description":"Natural language search query"}},"required":["query"]}"""),
                Annotations = new System.Text.Json.Nodes.JsonObject { ["readOnlyHint"] = true, ["openWorldHint"] = false },
            });
        }

        return McpResponse.Success(id, new { tools });
    }

    /// <summary>
    /// Tools the caller may use: <c>[AiRole]</c> satisfied and the endpoint's own authorization policies allowed.
    /// Best-effort hiding; the authoritative check runs again on every call.
    /// </summary>
    private async Task<List<Swagger.Discovery.AiToolBinding>> GetVisibleToolsAsync(HttpContext context, CancellationToken cancellationToken)
    {
        var tools = await _toolRegistry.GetToolsAsync(cancellationToken).ConfigureAwait(false);
        var visible = new List<Swagger.Discovery.AiToolBinding>(tools.Count);
        var decisions = new Dictionary<Endpoint, bool>(ReferenceEqualityComparer.Instance);
        var user = context.User;
        try
        {
            foreach (var tool in tools)
            {
                if (!McpAuthorization.SatisfiesRoles(user, tool.Descriptor.RequiredRoles))
                    continue;

                if (!decisions.TryGetValue(tool.Endpoint, out var allowed))
                {
                    try
                    {
                        allowed = await McpAuthorization.AuthorizeEndpointAsync(context, tool.Endpoint).ConfigureAwait(false)
                            == AuthorizationOutcome.Allowed;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Could not evaluate the authorization of MCP tool {ToolName}; hidden", tool.Descriptor.ToolName);
                        allowed = false;
                    }
                    finally
                    {
                        context.User = user; // policies with schemes replace the principal
                    }
                    decisions[tool.Endpoint] = allowed;
                }

                if (allowed)
                    visible.Add(tool);
            }
        }
        finally
        {
            context.User = user;
        }
        return visible;
    }

    private async Task<McpResponse> HandleToolsCallAsync(McpRequest request, HttpContext context, CancellationToken cancellationToken)
    {
        var id = request.Id;
        var toolName = request.Params?.Name;
        if (string.IsNullOrWhiteSpace(toolName))
            return McpResponse.Failure(id, McpErrorCodes.InvalidParams, "Missing tool name");

        var isRag = toolName == RagToolName && _ragSearch is not null;
        var tool = isRag ? null : await _toolRegistry.FindToolAsync(toolName!, cancellationToken).ConfigureAwait(false);
        if (!isRag && tool is null)
            return McpResponse.Failure(id, McpErrorCodes.InvalidParams, $"Unknown tool: {toolName}");

        // Unknown and role-restricted tools look the same to callers that may not use them.
        if (tool is not null && !McpAuthorization.SatisfiesRoles(context.User, tool.Descriptor.RequiredRoles))
            return McpResponse.Failure(id, McpErrorCodes.InvalidParams, $"Unknown tool: {toolName}");

        if (_options.EnableRateLimiting)
        {
            var limit = tool?.Descriptor.RateLimitPerMinute ?? _options.GlobalRateLimitPerMinute;
            var clientKey = (_options.ClientKeySelector ?? McpClientKey.Default)(context);
            if (!_rateLimiter.TryAcquire(clientKey + "\u001f" + toolName, limit))
                return ToolResult(id, McpToolCallResult.Error($"Rate limit exceeded for tool '{toolName}' ({limit} calls per minute). Try again later.", StatusCodes.Status429TooManyRequests));
        }

        if (isRag)
            return await HandleRagSearchAsync(request, context, cancellationToken).ConfigureAwait(false);

        var result = await _toolExecutor.ExecuteAsync(context, tool!, request.Params?.Arguments, cancellationToken).ConfigureAwait(false);
        return ToolResult(id, result);
    }

    private static McpResponse ToolResult(JsonElement? id, McpToolCallResult result)
        => McpResponse.Success(id, new
        {
            content = new[] { new { type = "text", text = result.Text } },
            isError = result.IsError,
        });

    private async Task<McpResponse> HandleRagSearchAsync(McpRequest request, HttpContext context, CancellationToken cancellationToken)
    {
        var id = request.Id;
        string? query = null;
        if (request.Params?.Arguments is { } args && args.TryGetValue("query", out var q) && q.ValueKind == JsonValueKind.String)
            query = q.GetString();

        if (string.IsNullOrWhiteSpace(query))
            return ToolResult(id, McpToolCallResult.Error("Invalid arguments: 'query' is required."));

        try
        {
            var visibleRoles = (await _discoveryProvider.DiscoverEndpointsAsync(cancellationToken).ConfigureAwait(false))
                .GroupBy(d => d.ToolName, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First().RequiredRoles, StringComparer.Ordinal);

            var results = await _ragSearch!.SearchAsync(query!, cancellationToken).ConfigureAwait(false);
            var formatted = results
                // Only endpoints that are still discoverable and whose [AiRole] the caller satisfies.
                .Where(r => visibleRoles.TryGetValue(r.Document.ToolName, out var roles)
                    && McpAuthorization.SatisfiesRoles(context.User, roles))
                .Select(r => new
                {
                    toolName = r.Document.ToolName,
                    method = r.Document.HttpMethod,
                    route = r.Document.Route,
                    category = r.Document.Category,
                    score = Math.Round(r.Score * 100) / 100,
                });

            return ToolResult(id, new McpToolCallResult { Text = JsonSerializer.Serialize(formatted) });
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "RAG search failed (trace id {TraceId})", context.TraceIdentifier);
            return ToolResult(id, McpToolCallResult.Error($"RAG search failed (trace id: {context.TraceIdentifier})."));
        }
    }

    private McpResponse HandlePromptsList(JsonElement? id)
    {
        var prompts = new List<object>
        {
            new { name = "list_available_tools", description = "Describes all available API tools and their capabilities" },
        };
        if (_ragSearch is not null)
            prompts.Add(new { name = "search_api", description = "Searches the API documentation for relevant endpoints" });
        return McpResponse.Success(id, new { prompts });
    }

    private async Task<McpResponse> HandlePromptsGetAsync(McpRequest request, HttpContext context, CancellationToken cancellationToken)
    {
        var id = request.Id;
        var promptName = request.Params?.Name;

        if (promptName == "list_available_tools")
        {
            var tools = await GetVisibleToolsAsync(context, cancellationToken).ConfigureAwait(false);
            var description = string.Join("\n", tools.Select(t => $"- **{t.Descriptor.ToolName}**: {t.Descriptor.Description ?? "No description"}"));

            return McpResponse.Success(id, new
            {
                messages = new[]
                {
                    new { role = "user", content = new { type = "text", text = $"Here are the available API tools:\n\n{description}" } },
                },
            });
        }

        if (promptName == "search_api" && _ragSearch is not null)
        {
            return McpResponse.Success(id, new
            {
                messages = new[]
                {
                    new { role = "user", content = new { type = "text", text = "Search the API documentation. Use the rag_search tool." } },
                },
            });
        }

        return McpResponse.Failure(id, McpErrorCodes.InvalidParams, $"Unknown prompt: {promptName}");
    }
}
