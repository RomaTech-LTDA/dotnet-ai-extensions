using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Readers;
using Romatech.Extensions.Ai.Shared.Abstractions;
using Romatech.Extensions.Ai.Shared.Models;
using Romatech.Extensions.Ai.Swagger.Configuration;
using Romatech.Extensions.Ai.Swagger.Schema;

namespace Romatech.Extensions.Ai.Swagger.Discovery;

/// <summary>
/// Discovers endpoints by reading the application's Swagger/OpenAPI specification.
/// </summary>
public sealed class SwaggerEndpointDiscoveryProvider : IEndpointDiscoveryProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMemoryCache _cache;
    private readonly SwaggerDiscoveryOptions _options;
    private readonly ILogger<SwaggerEndpointDiscoveryProvider> _logger;

    private const string CacheKey = "Romatech.Ai.Swagger.Endpoints";

    /// <summary>
    /// Initializes a new instance of the <see cref="SwaggerEndpointDiscoveryProvider"/> class.
    /// </summary>
    public SwaggerEndpointDiscoveryProvider(
        IHttpClientFactory httpClientFactory,
        IMemoryCache cache,
        IOptions<SwaggerDiscoveryOptions> options,
        ILogger<SwaggerEndpointDiscoveryProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _cache = cache;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AiEndpointDescriptor>> DiscoverEndpointsAsync(CancellationToken cancellationToken = default)
    {
        if (_options.EnableCaching && _cache.TryGetValue(CacheKey, out IReadOnlyList<AiEndpointDescriptor>? cached) && cached is not null)
        {
            _logger.LogDebug("Returning cached endpoint descriptors");
            return cached;
        }

        var endpoints = await DiscoverFromSwaggerAsync(cancellationToken);

        if (_options.EnableCaching)
        {
            var cacheOptions = new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(TimeSpan.FromSeconds(_options.CacheDurationSeconds));
            _cache.Set(CacheKey, endpoints, cacheOptions);
        }

        return endpoints;
    }

    private async Task<IReadOnlyList<AiEndpointDescriptor>> DiscoverFromSwaggerAsync(CancellationToken cancellationToken)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("RomatechAiSwagger");
            var response = await client.GetAsync(_options.SwaggerEndpoint, cancellationToken);
            response.EnsureSuccessStatusCode();

            var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var reader = new OpenApiStreamReader();
            var document = reader.Read(stream, out var diagnostic);

            if (diagnostic.Errors.Any())
            {
                _logger.LogWarning("OpenAPI document has {ErrorCount} errors", diagnostic.Errors.Count);
            }

            return ConvertToDescriptors(document);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to discover endpoints from Swagger at {Endpoint}", _options.SwaggerEndpoint);
            return Array.Empty<AiEndpointDescriptor>();
        }
    }

    private static List<AiEndpointDescriptor> ConvertToDescriptors(OpenApiDocument document)
    {
        var descriptors = new List<AiEndpointDescriptor>();

        foreach (var (path, pathItem) in document.Paths)
        {
            foreach (var (operationType, operation) in pathItem.Operations)
            {
                var httpMethod = operationType.ToString().ToUpperInvariant();
                var toolName = operation.OperationId ?? GenerateToolName(httpMethod, path);

                descriptors.Add(new AiEndpointDescriptor
                {
                    ToolName = toolName,
                    HttpMethod = httpMethod,
                    Route = path,
                    Description = operation.Summary ?? operation.Description,
                    OperationId = operation.OperationId,
                    InputSchema = OpenApiSchemaConverter.ConvertToJsonSchema(operation),
                    ExposureLevel = AiExposureLevel.ReadOnly
                });
            }
        }

        return descriptors;
    }

    private static string GenerateToolName(string method, string path)
    {
        var cleanPath = path.Replace("/", "_").Replace("{", "").Replace("}", "").Trim('_');
        return $"{method.ToLowerInvariant()}_{cleanPath}".ToLowerInvariant();
    }
}
