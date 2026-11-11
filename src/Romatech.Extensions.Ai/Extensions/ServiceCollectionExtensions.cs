using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Romatech.Extensions.Ai.Mcp.Configuration;
using Romatech.Extensions.Ai.Mcp.Http;
using Romatech.Extensions.Ai.Mcp.Security;
using Romatech.Extensions.Ai.Mcp.Services;
using Romatech.Extensions.Ai.Rag.Configuration;
using Romatech.Extensions.Ai.Rag.Embeddings;
using Romatech.Extensions.Ai.Rag.Indexing;
using Romatech.Extensions.Ai.Rag.Services;
using Romatech.Extensions.Ai.Shared.Abstractions;
using Romatech.Extensions.Ai.Swagger.Discovery;

namespace Romatech.Extensions.Ai.Extensions;

/// <summary>
/// Extension methods for registering Romatech.Extensions.Ai services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds MCP (Model Context Protocol) support. Endpoints marked with <c>[AiTool]</c> / <c>.AiTool()</c> become
    /// MCP tools that execute in-process with the caller's identity and the endpoint's own authorization policies.
    /// The MCP endpoint requires an authenticated caller by default (see <see cref="McpOptions.RequireAuthentication"/>).
    /// </summary>
    public static IServiceCollection UseMcp(this IServiceCollection services)
        => services.UseMcp(_ => { });

    /// <summary>
    /// Adds MCP (Model Context Protocol) support with custom configuration.
    /// </summary>
    public static IServiceCollection UseMcp(this IServiceCollection services, Action<McpOptions> configure)
    {
        if (services is null) throw new ArgumentNullException(nameof(services));
        if (configure is null) throw new ArgumentNullException(nameof(configure));

        services.Configure(configure);
        AddDiscovery(services);

        services.TryAddSingleton<McpToolRegistry>();
        services.TryAddSingleton<McpToolExecutor>();
        services.TryAddSingleton<McpRequestHandler>();
        services.TryAddSingleton<McpHttpHandler>();
        services.TryAddSingleton<IRateLimiter, SlidingWindowRateLimiter>();
        DecorateHttpContextAccessor(services);

        return services;
    }

    /// <summary>
    /// Wraps the application's <see cref="IHttpContextAccessor"/> (or a default one) so tools see their own request.
    /// An accessor registered later with <c>AddHttpContextAccessor()</c> is a no-op (TryAdd), so order does not matter.
    /// </summary>
    private static void DecorateHttpContextAccessor(IServiceCollection services)
    {
        if (services.Any(d => d.ServiceType == typeof(AccessorDecoratedMarker)))
            return; // already decorated
        services.AddSingleton<AccessorDecoratedMarker>();

        var existing = services.LastOrDefault(d => d.ServiceType == typeof(IHttpContextAccessor));

        Func<IServiceProvider, IHttpContextAccessor> inner = existing switch
        {
            { ImplementationInstance: IHttpContextAccessor instance } => _ => instance,
            { ImplementationFactory: { } factory } => sp => (IHttpContextAccessor)factory(sp),
            { ImplementationType: { } type } => sp => (IHttpContextAccessor)ActivatorUtilities.CreateInstance(sp, type),
            _ => _ => new HttpContextAccessor(),
        };

        services.RemoveAll<IHttpContextAccessor>();
        services.AddSingleton<IHttpContextAccessor>(sp => CreateAccessor(sp, inner));
    }

    private sealed class AccessorDecoratedMarker
    {
    }

    private static IHttpContextAccessor CreateAccessor(IServiceProvider services, Func<IServiceProvider, IHttpContextAccessor> inner)
        => new McpHttpContextAccessor(inner(services));

    /// <summary>
    /// Adds RAG (Retrieval Augmented Generation) support: every AI-visible endpoint is indexed for semantic search
    /// and, when MCP is enabled, exposed through the built-in <c>rag_search</c> tool.
    /// </summary>
    public static IServiceCollection UseRag(this IServiceCollection services)
        => services.UseRag(_ => { });

    /// <summary>
    /// Adds RAG support with custom configuration.
    /// </summary>
    public static IServiceCollection UseRag(this IServiceCollection services, Action<RagOptions> configure)
    {
        if (services is null) throw new ArgumentNullException(nameof(services));
        if (configure is null) throw new ArgumentNullException(nameof(configure));

        services.Configure(configure);
        AddDiscovery(services);

        services.TryAddSingleton<IEmbeddingProvider, LocalEmbeddingProvider>();
        services.TryAddSingleton<SemanticIndexer>();
        services.TryAddSingleton<RagSearchService>();

        return services;
    }

    private static void AddDiscovery(IServiceCollection services)
    {
        // ApiExplorer is how both MVC and Minimal API endpoints are described in-process
        // (it is what Swashbuckle / Microsoft.AspNetCore.OpenApi / NSwag read). Idempotent.
        services.AddEndpointsApiExplorer();

        services.TryAddSingleton<ApiExplorerEndpointDiscoveryProvider>();
        services.TryAddSingleton<IEndpointDiscoveryProvider>(sp => sp.GetRequiredService<ApiExplorerEndpointDiscoveryProvider>());
        services.TryAddSingleton<IAiToolBindingSource>(sp => sp.GetRequiredService<ApiExplorerEndpointDiscoveryProvider>());
    }
}
