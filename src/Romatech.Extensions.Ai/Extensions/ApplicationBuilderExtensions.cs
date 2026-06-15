using Microsoft.AspNetCore.Builder;
using Romatech.Extensions.Ai.Mcp.Middleware;

namespace Romatech.Extensions.Ai.Extensions;

/// <summary>
/// Extension methods for configuring the request pipeline with AI middleware.
/// </summary>
public static class ApplicationBuilderExtensions
{
    /// <summary>
    /// Adds the MCP endpoint middleware to the request pipeline.
    /// Handles POST /mcp requests (or custom route) for MCP protocol communication.
    /// </summary>
    public static IApplicationBuilder UseMcp(this IApplicationBuilder app)
    {
        return app.UseMiddleware<McpMiddleware>();
    }

    /// <summary>
    /// Adds the RAG initialization to the request pipeline.
    /// Triggers index building on first request if not already built.
    /// </summary>
    public static IApplicationBuilder UseRag(this IApplicationBuilder app)
    {
        // RAG initialization happens lazily on first search request.
        // This middleware is a no-op placeholder for future RAG pipeline features.
        return app;
    }
}
