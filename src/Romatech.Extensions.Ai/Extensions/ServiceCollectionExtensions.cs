using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Romatech.Extensions.Ai.Mcp.Configuration;
using Romatech.Extensions.Ai.Mcp.Middleware;
using Romatech.Extensions.Ai.Mcp.Security;
using Romatech.Extensions.Ai.Mcp.Services;
using Romatech.Extensions.Ai.Rag.Configuration;
using Romatech.Extensions.Ai.Rag.Embeddings;
using Romatech.Extensions.Ai.Rag.Indexing;
using Romatech.Extensions.Ai.Rag.Services;
using Romatech.Extensions.Ai.Shared.Abstractions;
using Romatech.Extensions.Ai.Swagger.Configuration;
using Romatech.Extensions.Ai.Swagger.Discovery;

namespace Romatech.Extensions.Ai.Extensions;

/// <summary>
/// Extension methods for registering Romatech.Extensions.Ai services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds MCP (Model Context Protocol) support to the application.
    /// Automatically discovers endpoints and exposes them as MCP tools.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection UseMcp(this IServiceCollection services)
    {
        return services.UseMcp(_ => { });
    }

    /// <summary>
    /// Adds MCP (Model Context Protocol) support with custom configuration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Configuration action for MCP options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection UseMcp(this IServiceCollection services, Action<McpOptions> configure)
    {
        services.Configure(configure);
        services.Configure<SwaggerDiscoveryOptions>(_ => { });

        // Core services
        services.AddMemoryCache();
        services.AddHttpClient("RomatechAiSwagger");
        services.AddHttpClient("RomatechAiMcpInternal");

        // Discovery
        services.TryAddSingleton<IEndpointDiscoveryProvider, SwaggerEndpointDiscoveryProvider>();

        // MCP services
        services.TryAddSingleton<McpToolRegistry>();
        services.TryAddSingleton<McpToolExecutor>();
        services.TryAddSingleton<McpRequestHandler>();
        services.TryAddSingleton<IRateLimiter, SlidingWindowRateLimiter>();

        return services;
    }

    /// <summary>
    /// Adds RAG (Retrieval Augmented Generation) support to the application.
    /// Automatically indexes endpoints for semantic search.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection UseRag(this IServiceCollection services)
    {
        return services.UseRag(_ => { });
    }

    /// <summary>
    /// Adds RAG support with custom configuration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Configuration action for RAG options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection UseRag(this IServiceCollection services, Action<RagOptions> configure)
    {
        services.Configure(configure);
        services.Configure<SwaggerDiscoveryOptions>(_ => { });

        // Core services
        services.AddMemoryCache();
        services.AddHttpClient("RomatechAiSwagger");

        // Discovery
        services.TryAddSingleton<IEndpointDiscoveryProvider, SwaggerEndpointDiscoveryProvider>();

        // RAG services
        services.TryAddSingleton<IEmbeddingProvider, LocalEmbeddingProvider>();
        services.TryAddSingleton<SemanticIndexer>();
        services.TryAddSingleton<RagSearchService>();

        return services;
    }
}
