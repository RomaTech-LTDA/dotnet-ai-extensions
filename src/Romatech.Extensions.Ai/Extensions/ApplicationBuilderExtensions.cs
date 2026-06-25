using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Romatech.Extensions.Ai.Mcp.Middleware;
using Romatech.Extensions.Ai.Mcp.Services;
using Romatech.Extensions.Ai.Rag.Services;

namespace Romatech.Extensions.Ai.Extensions;

/// <summary>
/// Extension methods for configuring the request pipeline with AI middleware.
/// </summary>
public static class ApplicationBuilderExtensions
{
    /// <summary>
    /// Adds the MCP endpoint middleware to the request pipeline.
    /// Handles POST /mcp requests (or custom route) for MCP protocol communication.
    /// Automatically wires RAG search if UseRag() was called.
    /// </summary>
    public static IApplicationBuilder UseMcp(this IApplicationBuilder app)
    {
        // Wire RAG to MCP handler if both are registered
        var handler = app.ApplicationServices.GetService<McpRequestHandler>();
        var ragSearch = app.ApplicationServices.GetService<RagSearchService>();
        if (handler is not null && ragSearch is not null)
        {
            handler.AttachRagSearch(ragSearch);
        }

        return app.UseMiddleware<McpMiddleware>();
    }

    /// <summary>
    /// Adds the RAG initialization to the request pipeline.
    /// Triggers index building on first search request if not already built.
    /// </summary>
    public static IApplicationBuilder UseRag(this IApplicationBuilder app)
    {
        // RAG initialization happens lazily on first search request.
        return app;
    }
}
