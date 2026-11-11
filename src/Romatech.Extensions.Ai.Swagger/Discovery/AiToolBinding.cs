using System.Text.Json;
using Microsoft.AspNetCore.Routing;
using Romatech.Extensions.Ai.Shared.Models;

namespace Romatech.Extensions.Ai.Swagger.Discovery;

/// <summary>
/// Where a tool argument is bound when the endpoint is invoked.
/// </summary>
public enum AiParameterSource
{
    /// <summary>Route value (path segment).</summary>
    Path,

    /// <summary>Query string.</summary>
    Query,

    /// <summary>Request header.</summary>
    Header,
}

/// <summary>
/// How the request body is represented in the tool input schema.
/// </summary>
public enum AiBodyMode
{
    /// <summary>The endpoint has no body.</summary>
    None,

    /// <summary>The body object's properties appear at the top level of the tool arguments.</summary>
    Flattened,

    /// <summary>The body is passed as a single argument (see <see cref="AiToolBinding.BodyArgumentName"/>).</summary>
    Nested,
}

/// <summary>
/// A non-body parameter of an executable tool.
/// </summary>
public sealed class AiParameterBinding
{
    /// <summary>The argument name in the tool input schema (also the route value / query key / header name).</summary>
    public string Name { get; init; } = default!;

    /// <summary>Where the value is bound.</summary>
    public AiParameterSource Source { get; init; }

    /// <summary>The CLR type the endpoint expects.</summary>
    public Type Type { get; init; } = typeof(string);

    /// <summary>Whether the argument must be supplied.</summary>
    public bool IsRequired { get; init; }

    /// <summary>For path parameters: whether it is a catch-all (<c>{*path}</c>) that may contain '/'.</summary>
    public bool IsCatchAll { get; init; }
}

/// <summary>
/// Everything needed to execute an endpoint in-process as an MCP tool.
/// </summary>
public sealed class AiToolBinding
{
    /// <summary>The AI-facing description of the endpoint.</summary>
    public AiEndpointDescriptor Descriptor { get; init; } = default!;

    /// <summary>The ASP.NET Core endpoint that is invoked. Never chosen from caller input.</summary>
    public RouteEndpoint Endpoint { get; init; } = default!;

    /// <summary>The HTTP method used for the in-process request.</summary>
    public string HttpMethod { get; init; } = default!;

    /// <summary>Path, query and header parameters.</summary>
    public IReadOnlyList<AiParameterBinding> Parameters { get; init; } = Array.Empty<AiParameterBinding>();

    /// <summary>How the body is represented in the arguments.</summary>
    public AiBodyMode BodyMode { get; init; }

    /// <summary>Whether the endpoint requires a body.</summary>
    public bool BodyRequired { get; init; }

    /// <summary>When <see cref="BodyMode"/> is <see cref="AiBodyMode.Nested"/>: the argument holding the body.</summary>
    public string? BodyArgumentName { get; init; }

    /// <summary>When <see cref="BodyMode"/> is <see cref="AiBodyMode.Flattened"/>: the JSON property names of the body.</summary>
    public IReadOnlyCollection<string> BodyPropertyNames { get; init; } = Array.Empty<string>();

    /// <summary>The serializer options the endpoint uses (MVC or Minimal API JSON options).</summary>
    public JsonSerializerOptions SerializerOptions { get; init; } = new(JsonSerializerDefaults.Web);
}
