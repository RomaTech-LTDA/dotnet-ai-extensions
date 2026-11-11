using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Romatech.Extensions.Ai.Mcp.Configuration;
using Romatech.Extensions.Ai.Mcp.Http;

namespace Romatech.Extensions.Ai.Mcp.Middleware;

/// <summary>
/// ASP.NET Core middleware that serves the MCP endpoint at <see cref="McpOptions.Route"/>.
/// Prefer <c>app.MapMcp()</c> when you want to attach endpoint conventions
/// (<c>RequireAuthorization</c>, <c>RequireCors</c>, <c>RequireRateLimiting</c>).
/// </summary>
public sealed class McpMiddleware
{
    private readonly RequestDelegate _next;
    private readonly McpHttpHandler _handler;
    private readonly PathString _route;

    /// <summary>
    /// Initializes a new instance of the <see cref="McpMiddleware"/> class.
    /// </summary>
    public McpMiddleware(RequestDelegate next, McpHttpHandler handler, IOptions<McpOptions> options)
    {
        _next = next;
        _handler = handler;
        _route = new PathString(options.Value.Route);
    }

    /// <summary>
    /// Serves MCP requests and passes everything else to the next middleware.
    /// </summary>
    public Task InvokeAsync(HttpContext context)
    {
        return context.Request.Path.Equals(_route, StringComparison.OrdinalIgnoreCase)
            ? _handler.HandleAsync(context)
            : _next(context);
    }
}
