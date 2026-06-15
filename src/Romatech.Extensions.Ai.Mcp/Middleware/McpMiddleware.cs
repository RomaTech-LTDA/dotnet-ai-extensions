using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Romatech.Extensions.Ai.Mcp.Configuration;
using Romatech.Extensions.Ai.Mcp.Protocol;
using Romatech.Extensions.Ai.Mcp.Services;

namespace Romatech.Extensions.Ai.Mcp.Middleware;

/// <summary>
/// ASP.NET Core middleware that handles MCP protocol requests.
/// </summary>
public sealed class McpMiddleware
{
    private readonly RequestDelegate _next;
    private readonly McpRequestHandler _handler;
    private readonly McpOptions _options;
    private readonly ILogger<McpMiddleware> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public McpMiddleware(
        RequestDelegate next,
        McpRequestHandler handler,
        IOptions<McpOptions> options,
        ILogger<McpMiddleware> logger)
    {
        _next = next;
        _handler = handler;
        _options = options.Value;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!IsMatchingRoute(context))
        {
            await _next(context);
            return;
        }

        if (context.Request.Method != "POST")
        {
            context.Response.StatusCode = 405;
            return;
        }

        McpRequest? request;
        try
        {
            request = await JsonSerializer.DeserializeAsync<McpRequest>(
                context.Request.Body, JsonOptions, context.RequestAborted);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Invalid MCP request JSON");
            context.Response.StatusCode = 400;
            await context.Response.WriteAsJsonAsync(
                McpResponse.Failure(null, -32700, "Parse error"), JsonOptions);
            return;
        }

        if (request is null)
        {
            context.Response.StatusCode = 400;
            await context.Response.WriteAsJsonAsync(
                McpResponse.Failure(null, -32600, "Invalid request"), JsonOptions);
            return;
        }

        var response = await _handler.HandleAsync(request, context.RequestAborted);
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(response, JsonOptions);
    }

    private bool IsMatchingRoute(HttpContext context)
    {
        return context.Request.Path.Equals(_options.Route, StringComparison.OrdinalIgnoreCase);
    }
}
