using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Romatech.Extensions.Ai.Mcp.Configuration;
using Romatech.Extensions.Ai.Mcp.Http;
using Romatech.Extensions.Ai.Mcp.Middleware;
using Romatech.Extensions.Ai.Metadata.Attributes;

namespace Romatech.Extensions.Ai.Extensions;

/// <summary>
/// Extension methods for exposing the MCP endpoint.
/// </summary>
public static class ApplicationBuilderExtensions
{
    /// <summary>
    /// Serves the MCP endpoint at <see cref="McpOptions.Route"/> (default <c>/mcp</c>) as middleware.
    /// </summary>
    public static IApplicationBuilder UseMcp(this IApplicationBuilder app)
    {
        if (app is null) throw new ArgumentNullException(nameof(app));
        EnsureRegistered(app.ApplicationServices);
        return app.UseMiddleware<McpMiddleware>();
    }

    /// <summary>
    /// Maps the MCP endpoint as a routed endpoint, so standard conventions apply:
    /// <c>app.MapMcp().RequireAuthorization("policy").RequireCors("policy")</c>.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="pattern">Route pattern; defaults to <see cref="McpOptions.Route"/>.</param>
    public static IEndpointConventionBuilder MapMcp(this IEndpointRouteBuilder endpoints, string? pattern = null)
    {
        if (endpoints is null) throw new ArgumentNullException(nameof(endpoints));
        var services = endpoints.ServiceProvider;
        EnsureRegistered(services);

        var route = pattern ?? services.GetRequiredService<IOptions<McpOptions>>().Value.Route;
        var handler = services.GetRequiredService<McpHttpHandler>();
        return endpoints.Map(route, handler.HandleAsync)
            .WithMetadata(new AiHiddenAttribute())
            .WithDisplayName("MCP");
    }

    /// <summary>
    /// Kept for compatibility. RAG is initialized lazily on the first search; this call is optional.
    /// </summary>
    public static IApplicationBuilder UseRag(this IApplicationBuilder app) => app;

    private static void EnsureRegistered(IServiceProvider services)
    {
        if (services.GetService<McpHttpHandler>() is null)
        {
            throw new InvalidOperationException(
                "MCP services are not registered. Call builder.Services.UseMcp() before app.UseMcp()/app.MapMcp().");
        }
    }
}
