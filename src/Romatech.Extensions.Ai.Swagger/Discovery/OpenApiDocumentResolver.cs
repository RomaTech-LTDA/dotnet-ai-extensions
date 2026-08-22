using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Romatech.Extensions.Ai.Swagger.Discovery;

/// <summary>
/// Resolves the OpenAPI document endpoint by inspecting registered services in the DI container.
/// Supports auto-detection for Swashbuckle, Microsoft.AspNetCore.OpenApi, and Scalar setups.
/// Falls back to well-known routes if no provider is detected.
/// Allows manual override via configuration.
/// </summary>
public sealed class OpenApiDocumentResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<OpenApiDocumentResolver> _logger;

    /// <summary>
    /// Well-known OpenAPI document routes in order of precedence.
    /// </summary>
    private static readonly string[] WellKnownRoutes = new[]
    {
        "/swagger/v1/swagger.json",         // Swashbuckle default
        "/openapi/v1.json",                 // Microsoft.AspNetCore.OpenApi / Scalar default
        "/swagger/v1/swagger.yaml",         // Swashbuckle YAML
    };

    /// <summary>
    /// Service type names used to detect which OpenAPI generator is registered.
    /// </summary>
    private static readonly (string TypeName, string Route, string ProviderName)[] KnownProviders = new[]
    {
        ("Swashbuckle.AspNetCore.Swagger.ISwaggerProvider", "/swagger/v1/swagger.json", "Swashbuckle"),
        ("Swashbuckle.AspNetCore.SwaggerGen.ISwaggerProvider", "/swagger/v1/swagger.json", "Swashbuckle"),
        ("Microsoft.AspNetCore.OpenApi.IOpenApiDocumentService", "/openapi/v1.json", "Microsoft.AspNetCore.OpenApi"),
    };

    public OpenApiDocumentResolver(
        IServiceProvider serviceProvider,
        ILogger<OpenApiDocumentResolver> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <summary>
    /// Resolves the OpenAPI document endpoint.
    /// If a manual endpoint is provided, it takes precedence.
    /// Otherwise, inspects registered services to determine the correct route.
    /// </summary>
    /// <param name="manualEndpoint">Optional user-provided endpoint override.</param>
    /// <returns>The resolved OpenAPI document route.</returns>
    public string ResolveEndpoint(string? manualEndpoint = null)
    {
        // Manual override takes highest priority
        if (!string.IsNullOrWhiteSpace(manualEndpoint))
        {
            _logger.LogDebug("Using manually configured OpenAPI endpoint: {Endpoint}", manualEndpoint);
            return manualEndpoint;
        }

        // Auto-detect by inspecting registered services
        var detectedRoute = DetectFromRegisteredServices();
        if (detectedRoute is not null)
        {
            return detectedRoute;
        }

        // Fallback to Swashbuckle default (most common)
        _logger.LogWarning(
            "No OpenAPI provider detected in DI. Falling back to default route: {Route}. " +
            "Ensure your application registers an OpenAPI generator (Swashbuckle, Microsoft.AspNetCore.OpenApi, etc.) " +
            "or configure the endpoint manually via McpOptions.OpenApiEndpoint.",
            WellKnownRoutes[0]);

        return WellKnownRoutes[0];
    }

    private string? DetectFromRegisteredServices()
    {
        foreach (var (typeName, route, providerName) in KnownProviders)
        {
            if (IsServiceRegistered(typeName))
            {
                _logger.LogInformation(
                    "Detected {Provider} as OpenAPI generator. Using endpoint: {Route}",
                    providerName, route);
                return route;
            }
        }

        return null;
    }

    private bool IsServiceRegistered(string typeFullName)
    {
        try
        {
            // Try to resolve the type by name without a compile-time reference
            var type = AppDomain.CurrentDomain
                .GetAssemblies()
                .Select(a =>
                {
                    try { return a.GetType(typeFullName); }
                    catch { return null; }
                })
                .FirstOrDefault(t => t is not null);

            if (type is null)
                return false;

            // Check if the service is actually registered
            var service = _serviceProvider.GetService(type);
            return service is not null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error checking for service type {TypeName}", typeFullName);
            return false;
        }
    }
}
