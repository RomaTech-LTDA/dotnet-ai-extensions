using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Romatech.Extensions.Ai.Metadata.Resolution;
using Romatech.Extensions.Ai.Shared.Abstractions;
using Romatech.Extensions.Ai.Shared.Models;
using Romatech.Extensions.Ai.Swagger.Schema;

namespace Romatech.Extensions.Ai.Swagger.Discovery;

/// <summary>
/// Discovers endpoints in-process from ASP.NET Core's ApiExplorer (the same model every OpenAPI
/// generator reads) and the endpoint metadata. No OpenAPI document is downloaded or parsed, so it works
/// with Swashbuckle, Microsoft.AspNetCore.OpenApi (OpenAPI.NET 1.x or 2.x), NSwag, Scalar or no generator at all.
/// Each executable tool is bound to the concrete <see cref="RouteEndpoint"/> it represents.
/// </summary>
public sealed class ApiExplorerEndpointDiscoveryProvider : IEndpointDiscoveryProvider, IAiToolBindingSource
{
    private static readonly HashSet<string> ReservedHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization", "Proxy-Authorization", "Cookie", "Host", "Origin", "Referer",
        "Content-Type", "Content-Length", "Content-Encoding", "Transfer-Encoding", "Connection",
        "Keep-Alive", "Upgrade", "TE", "Trailer", "Expect", "Forwarded", "X-Forwarded-For",
        "X-Forwarded-Host", "X-Forwarded-Proto", "X-Forwarded-Prefix", "X-Real-IP",
        "Mcp-Session-Id", "MCP-Protocol-Version",
    };

    private readonly IServiceProvider _services;
    private readonly ILogger<ApiExplorerEndpointDiscoveryProvider> _logger;
    private readonly object _gate = new();
    private Snapshot? _snapshot;

    /// <summary>
    /// Initializes a new instance of the <see cref="ApiExplorerEndpointDiscoveryProvider"/> class.
    /// </summary>
    public ApiExplorerEndpointDiscoveryProvider(IServiceProvider services, ILogger<ApiExplorerEndpointDiscoveryProvider> logger)
    {
        _services = services;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<AiEndpointDescriptor>> DiscoverEndpointsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(GetSnapshot().Descriptors);

    /// <inheritdoc />
    public Task<IReadOnlyList<AiToolBinding>> GetToolBindingsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(GetSnapshot().Bindings);

    private Snapshot GetSnapshot()
    {
        var current = Volatile.Read(ref _snapshot);
        if (current is not null && !current.ChangeToken.HasChanged)
            return current;

        lock (_gate)
        {
            current = _snapshot;
            if (current is not null && !current.ChangeToken.HasChanged)
                return current;

            current = Build();
            Volatile.Write(ref _snapshot, current);
            return current;
        }
    }

    private Snapshot Build()
    {
        var dataSource = _services.GetService<EndpointDataSource>();
        var actionDescriptors = _services.GetService<Microsoft.AspNetCore.Mvc.Infrastructure.IActionDescriptorCollectionProvider>();
        if (dataSource is null || actionDescriptors is null)
        {
            _logger.LogWarning(
                "Romatech.Extensions.Ai: endpoint routing or ApiExplorer is not registered; no endpoints can be discovered. " +
                "Call services.UseMcp()/UseRag() (which add ApiExplorer) and map your endpoints.");
            return new Snapshot(Array.Empty<AiEndpointDescriptor>(), Array.Empty<AiToolBinding>(), Microsoft.Extensions.FileProviders.NullChangeToken.Singleton);
        }

        // Read the token before the endpoints so a concurrent change is never missed.
        var changeToken = dataSource.GetChangeToken();
        var routeEndpoints = dataSource.Endpoints.OfType<RouteEndpoint>().ToList();
        var byAction = new Dictionary<ActionDescriptor, RouteEndpoint>(ReferenceEqualityComparer.Instance);
        foreach (var endpoint in routeEndpoints)
        {
            if (endpoint.Metadata.GetMetadata<ActionDescriptor>() is { } action && !byAction.ContainsKey(action))
                byAction[action] = endpoint;
        }

        var candidates = new List<Candidate>();
        // A fresh provider every time the endpoints change: the singleton IApiDescriptionGroupCollectionProvider caches
        // by action-descriptor version and would miss Minimal API endpoints added after the first call.
        var apiExplorer = new ApiDescriptionGroupCollectionProvider(actionDescriptors, _services.GetServices<IApiDescriptionProvider>());
        foreach (var group in apiExplorer.ApiDescriptionGroups.Items)
        {
            foreach (var description in group.Items)
            {
                var method = description.HttpMethod?.ToUpperInvariant();
                if (string.IsNullOrEmpty(method))
                    continue;

                var endpoint = ResolveEndpoint(description, method!, byAction, routeEndpoints);
                if (endpoint is null)
                {
                    if (description.ActionDescriptor.EndpointMetadata?.OfType<Metadata.Attributes.AiToolAttribute>().Any() == true)
                        _logger.LogWarning("AI tool {Method} {Path} could not be bound to a unique route endpoint; it is not exposed", method, description.RelativePath);
                    else
                        _logger.LogDebug("No route endpoint matches {Method} {Path}; skipped", method, description.RelativePath);
                    continue;
                }

                if (EndpointMetadataResolver.ResolveExposureLevel(endpoint.Metadata) == AiExposureLevel.Hidden)
                    continue;

                candidates.Add(new Candidate(description, endpoint, method!, "/" + (description.RelativePath ?? string.Empty).TrimStart('/')));
            }
        }

        // Deterministic order so tool-name collisions always resolve the same way.
        candidates.Sort((a, b) =>
        {
            var c = string.CompareOrdinal(a.Route, b.Route);
            return c != 0 ? c : string.CompareOrdinal(a.Method, b.Method);
        });

        var descriptors = new List<AiEndpointDescriptor>();
        var bindings = new List<AiToolBinding>();
        var usedNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var candidate in candidates)
        {
            try
            {
                var built = BuildCandidate(candidate, usedNames);
                if (built.Descriptor is not null) descriptors.Add(built.Descriptor);
                if (built.Binding is not null) bindings.Add(built.Binding);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not describe {Method} {Route} for AI; skipped", candidate.Method, candidate.Route);
            }
        }

        _logger.LogInformation(
            "Romatech.Extensions.Ai discovered {EndpointCount} AI-visible endpoints ({ToolCount} executable tools)",
            descriptors.Count, bindings.Count);

        return new Snapshot(descriptors, bindings, changeToken);
    }

    private (AiEndpointDescriptor? Descriptor, AiToolBinding? Binding) BuildCandidate(Candidate candidate, HashSet<string> usedNames)
    {
        var (description, endpoint, method, route) = candidate;
        var metadata = endpoint.Metadata;
        var exposure = EndpointMetadataResolver.ResolveExposureLevel(metadata);
        var serializerOptions = ResolveSerializerOptions(endpoint);

        var toolName = ResolveToolName(metadata, description, method, route);
        if (!usedNames.Add(toolName))
        {
            _logger.LogWarning(
                "Duplicate AI tool name '{ToolName}' for {Method} {Route}; the endpoint is skipped. Give it a unique [AiTool(\"name\")].",
                toolName, method, route);
            return (null, null);
        }

        string? unsupportedReason = null;
        if (metadata.GetMetadata<IHostMetadata>() is { Hosts.Count: > 0 } hostMetadata && !hostMetadata.Hosts.Contains("*"))
            unsupportedReason = "host-restricted endpoints (RequireHost) are not exposed as MCP tools";

        var parameters = new List<AiParameterBinding>();
        var properties = new JsonObject();
        var required = new JsonArray();
        ApiParameterDescription? bodyParameter = null;

        var routeParameters = endpoint.RoutePattern.Parameters
            .ToDictionary(p => p.Name, p => p, StringComparer.OrdinalIgnoreCase);
        var describedPath = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var parameter in description.ParameterDescriptions)
        {
            var source = parameter.Source;
            var type = parameter.Type ?? parameter.ModelMetadata?.ModelType ?? typeof(string);

            if (source == BindingSource.Body)
            {
                bodyParameter ??= parameter;
                continue;
            }

            if (source == BindingSource.Form || source == BindingSource.FormFile)
            {
                unsupportedReason ??= "form/file parameters are not supported over MCP";
                continue;
            }

            if (source == BindingSource.Services || source == BindingSource.Special)
                continue;

            if (source == BindingSource.Header)
            {
                if (ReservedHeaders.Contains(parameter.Name) || parameter.Name.StartsWith("Sec-", StringComparison.OrdinalIgnoreCase))
                {
                    if (parameter.IsRequired) unsupportedReason ??= $"header '{parameter.Name}' cannot be set by an AI client";
                    continue;
                }
                AddParameter(parameter.Name, AiParameterSource.Header, type, parameter.IsRequired, isCatchAll: false);
                continue;
            }

            if (source == BindingSource.Path || routeParameters.ContainsKey(parameter.Name))
            {
                if (!routeParameters.TryGetValue(parameter.Name, out var routeParameter))
                    continue;
                describedPath.Add(routeParameter.Name);
                AddParameter(routeParameter.Name, AiParameterSource.Path, type,
                    !routeParameter.IsOptional && routeParameter.Default is null, routeParameter.IsCatchAll);
                continue;
            }

            // Query, ModelBinding (MVC without [ApiController]), Custom, or unknown: bound from the query string.
            AddParameter(parameter.Name, AiParameterSource.Query, type, parameter.IsRequired, isCatchAll: false);
        }

        // Route parameters the handler does not bind still need a value to address the endpoint.
        foreach (var routeParameter in endpoint.RoutePattern.Parameters)
        {
            if (describedPath.Contains(routeParameter.Name) || endpoint.RoutePattern.RequiredValues.ContainsKey(routeParameter.Name))
                continue;
            AddParameter(routeParameter.Name, AiParameterSource.Path, typeof(string),
                !routeParameter.IsOptional && routeParameter.Default is null, routeParameter.IsCatchAll);
        }

        var bodyMode = AiBodyMode.None;
        string? bodyArgumentName = null;
        var bodyPropertyNames = new List<string>();
        var bodyRequired = false;

        if (bodyParameter is not null)
        {
            var bodyType = bodyParameter.Type ?? bodyParameter.ModelMetadata?.ModelType ?? typeof(object);
            var bodySchema = JsonSchemaGenerator.Generate(bodyType, serializerOptions);
            bodyRequired = bodyParameter.IsRequired;

            if (bodySchema["properties"] is JsonObject bodyProperties && bodyProperties.Count > 0
                && bodyProperties.All(p => !properties.ContainsKey(p.Key)))
            {
                bodyMode = AiBodyMode.Flattened;
                foreach (var (name, schema) in bodyProperties.ToList())
                {
                    bodyProperties.Remove(name);
                    properties[name] = schema;
                    bodyPropertyNames.Add(name);
                }
                if (bodySchema["required"] is JsonArray bodyRequiredProperties)
                {
                    foreach (var name in bodyRequiredProperties)
                    {
                        if (name is not null) required.Add(name.GetValue<string>());
                    }
                }
            }
            else
            {
                bodyMode = AiBodyMode.Nested;
                bodyArgumentName = properties.ContainsKey("body") ? "requestBody" : "body";
                bodySchema["description"] = "Request body.";
                properties[bodyArgumentName] = bodySchema;
                if (bodyRequired) required.Add(bodyArgumentName);
            }
        }

        var inputSchema = new JsonObject { ["type"] = "object", ["properties"] = properties };
        if (required.Count > 0) inputSchema["required"] = required;

        var roles = EndpointMetadataResolver.ResolveRoles(metadata);
        var descriptor = new AiEndpointDescriptor
        {
            ToolName = toolName,
            HttpMethod = method,
            Route = route,
            Description = ResolveDescription(metadata),
            Category = EndpointMetadataResolver.ResolveCategory(metadata),
            RequiredRole = roles.Count > 0 ? roles[0] : null,
            RequiredRoles = roles,
            RateLimitPerMinute = EndpointMetadataResolver.ResolveRateLimit(metadata),
            ContextPriority = EndpointMetadataResolver.ResolveContextPriority(metadata),
            OperationId = metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName,
            InputSchema = inputSchema.ToJsonString(),
            ExposureLevel = exposure,
        };

        if (exposure != AiExposureLevel.Executable)
            return (descriptor, null);

        if (unsupportedReason is not null)
        {
            _logger.LogWarning("AI tool '{ToolName}' ({Method} {Route}) is not executable: {Reason}", toolName, method, route, unsupportedReason);
            return (descriptor, null);
        }

        var binding = new AiToolBinding
        {
            Descriptor = descriptor,
            Endpoint = endpoint,
            HttpMethod = method,
            Parameters = parameters,
            BodyMode = bodyMode,
            BodyRequired = bodyRequired,
            BodyArgumentName = bodyArgumentName,
            BodyPropertyNames = bodyPropertyNames,
            SerializerOptions = serializerOptions,
        };
        return (descriptor, binding);

        void AddParameter(string name, AiParameterSource parameterSource, Type type, bool isRequired, bool isCatchAll)
        {
            if (properties.ContainsKey(name))
                return;
            var schema = JsonSchemaGenerator.Generate(type, serializerOptions, enumsAsStrings: true);
            if (parameterSource == AiParameterSource.Path && isCatchAll)
                schema["description"] = "May contain '/'-separated segments.";
            properties[name] = schema;
            if (isRequired) required.Add(name);
            parameters.Add(new AiParameterBinding
            {
                Name = name,
                Source = parameterSource,
                Type = type,
                IsRequired = isRequired,
                IsCatchAll = isCatchAll,
            });
        }
    }

    private static RouteEndpoint? ResolveEndpoint(
        ApiDescription description,
        string method,
        Dictionary<ActionDescriptor, RouteEndpoint> byAction,
        List<RouteEndpoint> endpoints)
    {
        // MVC: the endpoint carries the very ActionDescriptor ApiExplorer describes.
        if (byAction.TryGetValue(description.ActionDescriptor, out var mvcEndpoint))
            return mvcEndpoint;

        // Minimal APIs: match on HTTP method + route structure, then on display name if ambiguous.
        RoutePattern parsed;
        try
        {
            parsed = RoutePatternFactory.Parse(description.RelativePath ?? string.Empty);
        }
        catch (RoutePatternException)
        {
            return null;
        }

        var matches = endpoints
            .Where(e => e.Metadata.GetMetadata<ActionDescriptor>() is null
                && AllowsMethod(e, method)
                && RoutePatternMatcher.AreEquivalent(e.RoutePattern, parsed))
            .ToList();

        if (matches.Count == 1)
            return matches[0];

        if (matches.Count > 1)
        {
            // EndpointMetadataApiDescriptionProvider copies the endpoint's own metadata objects: compare by identity.
            var described = description.ActionDescriptor.EndpointMetadata;
            if (described is { Count: > 0 })
            {
                var exact = matches.Where(e => e.Metadata.Count == described.Count
                    && e.Metadata.Select((m, i) => ReferenceEquals(m, described[i])).All(same => same)).ToList();
                if (exact.Count == 1)
                    return exact[0];
            }

            var byName = matches.Where(e => e.DisplayName == description.ActionDescriptor.DisplayName).ToList();
            if (byName.Count == 1)
                return byName[0];
        }

        return null;
    }

    private static bool AllowsMethod(RouteEndpoint endpoint, string method)
    {
        var methods = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods;
        return methods is null || methods.Count == 0 || methods.Any(m => string.Equals(m, method, StringComparison.OrdinalIgnoreCase));
    }

    private JsonSerializerOptions ResolveSerializerOptions(RouteEndpoint endpoint)
    {
        if (endpoint.Metadata.GetMetadata<ControllerActionDescriptor>() is not null)
        {
            var mvc = _services.GetService<IOptions<Microsoft.AspNetCore.Mvc.JsonOptions>>()?.Value.JsonSerializerOptions;
            if (mvc is not null) return mvc;
        }

        return _services.GetService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>()?.Value.SerializerOptions
            ?? new JsonSerializerOptions(JsonSerializerDefaults.Web);
    }

    private static string? ResolveDescription(EndpointMetadataCollection metadata)
    {
        var description = EndpointMetadataResolver.ResolveDescription(metadata);
#if NET7_0_OR_GREATER
        description ??= metadata.GetMetadata<Microsoft.AspNetCore.Http.Metadata.IEndpointSummaryMetadata>()?.Summary;
        description ??= metadata.GetMetadata<Microsoft.AspNetCore.Http.Metadata.IEndpointDescriptionMetadata>()?.Description;
#endif
        return string.IsNullOrWhiteSpace(description) ? null : description;
    }

    private string ResolveToolName(EndpointMetadataCollection metadata, ApiDescription description, string method, string route)
    {
        var explicitName = EndpointMetadataResolver.ResolveToolName(metadata);
        var raw = explicitName
            ?? metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName
            ?? description.ActionDescriptor.AttributeRouteInfo?.Name
            ?? GenerateToolName(method, route);

        var sanitized = SanitizeToolName(raw);
        if (explicitName is not null && !string.Equals(sanitized, explicitName, StringComparison.Ordinal))
        {
            _logger.LogWarning("AI tool name '{Name}' contains characters MCP clients reject; using '{Sanitized}'", explicitName, sanitized);
        }
        return sanitized;
    }

    private static string GenerateToolName(string method, string route)
    {
        var path = route.Replace("{", string.Empty).Replace("}", string.Empty).Replace("*", string.Empty);
        return $"{method}_{path}".ToLowerInvariant();
    }

    /// <summary>
    /// Restricts a tool name to <c>[A-Za-z0-9_.-]</c>, at most 64 characters (the portable subset accepted by MCP clients).
    /// </summary>
    internal static string SanitizeToolName(string name)
    {
        var builder = new StringBuilder(name.Length);
        var lastWasUnderscore = false;
        foreach (var c in name)
        {
            var valid = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c is '-' or '.' or '_';
            var next = valid ? c : '_';
            if (next == '_' && lastWasUnderscore) continue;
            builder.Append(next);
            lastWasUnderscore = next == '_';
        }

        var result = builder.ToString().Trim('_');
        if (result.Length == 0)
            result = "tool";

        if (result.Length > 64)
        {
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(name));
            var suffix = string.Concat(hash.Take(4).Select(b => b.ToString("x2", System.Globalization.CultureInfo.InvariantCulture)));
            result = result.Substring(0, 55) + "_" + suffix;
        }

        return result;
    }

    private sealed record Candidate(ApiDescription Description, RouteEndpoint Endpoint, string Method, string Route);

    private sealed record Snapshot(
        IReadOnlyList<AiEndpointDescriptor> Descriptors,
        IReadOnlyList<AiToolBinding> Bindings,
        IChangeToken ChangeToken);
}
