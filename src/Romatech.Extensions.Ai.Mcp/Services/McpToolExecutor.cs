using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Romatech.Extensions.Ai.Mcp.Configuration;
using Romatech.Extensions.Ai.Mcp.Http;
using Romatech.Extensions.Ai.Mcp.Internal;
using Romatech.Extensions.Ai.Mcp.Security;
using Romatech.Extensions.Ai.Swagger.Discovery;

namespace Romatech.Extensions.Ai.Mcp.Services;

/// <summary>
/// Result of a tool execution, mapped to an MCP <c>CallToolResult</c>.
/// </summary>
public sealed class McpToolCallResult
{
    /// <summary>Text returned to the AI client.</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>Whether the call failed (non-2xx status, authorization failure, invalid arguments, timeout...).</summary>
    public bool IsError { get; init; }

    /// <summary>The HTTP status produced by the endpoint, when it ran.</summary>
    public int? StatusCode { get; init; }

    internal static McpToolCallResult Error(string text, int? statusCode = null)
        => new() { Text = text, IsError = true, StatusCode = statusCode };
}

/// <summary>
/// Executes MCP tool calls in-process: the bound endpoint is invoked directly, with the caller's identity,
/// connection and headers, after the endpoint's own authorization policies and route constraints are checked.
/// No HTTP loopback, no base URL, and the target endpoint can never be chosen from caller input.
/// </summary>
public sealed class McpToolExecutor
{
    private static readonly HashSet<string> StrippedRequestHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Content-Type", "Content-Length", "Content-Encoding", "Transfer-Encoding", "Connection", "Keep-Alive",
        "Upgrade", "TE", "Trailer", "Expect", "Accept", "Accept-Encoding", "Mcp-Session-Id",
        "MCP-Protocol-Version", "Last-Event-ID",
    };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly McpOptions _options;
    private readonly ILogger<McpToolExecutor> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="McpToolExecutor"/> class.
    /// </summary>
    public McpToolExecutor(IServiceScopeFactory scopeFactory, IOptions<McpOptions> options, ILogger<McpToolExecutor> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Executes a tool on behalf of the MCP caller.
    /// </summary>
    public async Task<McpToolCallResult> ExecuteAsync(
        HttpContext caller,
        AiToolBinding tool,
        IReadOnlyDictionary<string, JsonElement>? arguments,
        CancellationToken cancellationToken = default)
    {
        if (caller is null) throw new ArgumentNullException(nameof(caller));
        if (tool is null) throw new ArgumentNullException(nameof(tool));

        if (!TryBindArguments(tool, arguments ?? new Dictionary<string, JsonElement>(), out var bound, out var bindError))
            return McpToolCallResult.Error($"Invalid arguments: {bindError}");

        if (tool.Endpoint.RequestDelegate is null)
            return McpToolCallResult.Error("The endpoint cannot be invoked.");

        // The timeout is enforced even if the endpoint ignores RequestAborted: the call is answered at the deadline and
        // the abandoned invocation keeps its scope until it really ends (then everything is disposed and logged).
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.ToolTimeout);

        var scope = _scopeFactory.CreateAsyncScope();
        var responseBuffer = new BoundedWriteStream(_options.MaxToolResponseBytes);
        var responseFeature = new ToolResponseFeature();
        var context = CreateContext(caller, tool, bound, scope.ServiceProvider, responseBuffer, responseFeature, timeout.Token);

        var invocation = InvokeAsync(context, tool, bound);
        var deadline = Task.Delay(Timeout.Infinite, timeout.Token);
        var first = await Task.WhenAny(invocation, deadline).ConfigureAwait(false);

        if (first != invocation)
        {
            _logger.LogWarning("MCP tool {ToolName} did not finish within {Timeout}", tool.Descriptor.ToolName, _options.ToolTimeout);
            _ = invocation.ContinueWith(
                async t =>
                {
                    if (t.IsFaulted && t.Exception?.InnerException is { } inner && inner is not OperationCanceledException)
                        _logger.LogError(inner, "Abandoned MCP tool {ToolName} failed after its timeout", tool.Descriptor.ToolName);
                    await CleanupAsync(responseFeature, scope, timeout).ConfigureAwait(false);
                },
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.Default).Unwrap();

            return cancellationToken.IsCancellationRequested
                ? McpToolCallResult.Error("The request was cancelled.")
                : McpToolCallResult.Error($"The tool did not finish within {_options.ToolTimeout.TotalSeconds:0.#} seconds.", StatusCodes.Status504GatewayTimeout);
        }

        try
        {
            var refused = await invocation.ConfigureAwait(false);
            return refused ?? BuildResult(context.Response, responseBuffer);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("MCP tool {ToolName} timed out after {Timeout}", tool.Descriptor.ToolName, _options.ToolTimeout);
            return McpToolCallResult.Error($"The tool did not finish within {_options.ToolTimeout.TotalSeconds:0.#} seconds.", StatusCodes.Status504GatewayTimeout);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "MCP tool {ToolName} threw an unhandled exception (trace id {TraceId})",
                tool.Descriptor.ToolName, caller.TraceIdentifier);
            return McpToolCallResult.Error(
                $"HTTP 500 Internal Server Error (trace id: {caller.TraceIdentifier}).", StatusCodes.Status500InternalServerError);
        }
        finally
        {
            await CleanupAsync(responseFeature, scope, timeout).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Runs on its own async frame so the tool context set on <see cref="McpHttpContextAccessor"/> never leaks back
    /// to the MCP request. Returns a result when the call is refused before reaching the endpoint.
    /// </summary>
    private async Task<McpToolCallResult?> InvokeAsync(DefaultHttpContext context, AiToolBinding tool, BoundArguments bound)
    {
        McpHttpContextAccessor.EnterToolContext(context);

        if (!SatisfiesRouteConstraints(context, tool.Endpoint.RoutePattern, bound.RouteValues, out var constraintError))
            return McpToolCallResult.Error($"Invalid arguments: {constraintError}", StatusCodes.Status404NotFound);

        var outcome = await McpAuthorization.AuthorizeEndpointAsync(context, tool.Endpoint).ConfigureAwait(false);
        if (outcome == AuthorizationOutcome.Unauthenticated)
            return McpToolCallResult.Error("HTTP 401 Unauthorized: the caller is not authenticated for this tool.", StatusCodes.Status401Unauthorized);
        if (outcome == AuthorizationOutcome.Forbidden)
            return McpToolCallResult.Error("HTTP 403 Forbidden: the caller is not allowed to use this tool.", StatusCodes.Status403Forbidden);

        if (_options.AuthorizeToolCall is { } authorizeToolCall
            && !await authorizeToolCall(context, tool).ConfigureAwait(false))
            return McpToolCallResult.Error("HTTP 403 Forbidden: the caller is not allowed to use this tool.", StatusCodes.Status403Forbidden);

        await tool.Endpoint.RequestDelegate!(context).ConfigureAwait(false);
        await context.Features.Get<IHttpResponseBodyFeature>()!.CompleteAsync().ConfigureAwait(false);
        return null;
    }

    private async Task CleanupAsync(ToolResponseFeature responseFeature, AsyncServiceScope scope, CancellationTokenSource timeout)
    {
        try
        {
            await responseFeature.FireOnCompletedAsync(_logger).ConfigureAwait(false);
            await scope.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Disposing the services of an MCP tool call failed");
        }
        finally
        {
            timeout.Dispose();
        }
    }

    private McpToolCallResult BuildResult(HttpResponse response, BoundedWriteStream buffer)
    {
        var status = response.StatusCode;
        var isError = status < 200 || status >= 300;
        var bytes = buffer.ToArray();

        string body;
        if (bytes.Length == 0)
            body = string.Empty;
        else if (IsTextual(response.ContentType))
            body = Encoding.UTF8.GetString(bytes);
        else
            body = $"[{buffer.TotalBytesWritten} bytes of {response.ContentType} omitted]";

        if (buffer.Truncated)
            body += $"\n[response truncated: {buffer.TotalBytesWritten} bytes, limit {_options.MaxToolResponseBytes}]";

        string text;
        if (isError)
            text = $"HTTP {status} {ReasonPhrases.GetReasonPhrase(status)}".TrimEnd() + (body.Length > 0 ? "\n" + body : string.Empty);
        else
            text = body.Length > 0 ? body : $"HTTP {status} {ReasonPhrases.GetReasonPhrase(status)}".TrimEnd();

        return new McpToolCallResult { Text = text, IsError = isError, StatusCode = status };
    }

    private static bool IsTextual(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
            return true;
        var mediaType = contentType!.Split(';')[0].Trim().ToLowerInvariant();
        return mediaType.StartsWith("text/", StringComparison.Ordinal)
            || mediaType is "application/json" or "application/xml" or "application/javascript" or "application/x-www-form-urlencoded"
            || mediaType.EndsWith("+json", StringComparison.Ordinal)
            || mediaType.EndsWith("+xml", StringComparison.Ordinal);
    }

    private static DefaultHttpContext CreateContext(
        HttpContext caller,
        AiToolBinding tool,
        BoundArguments bound,
        IServiceProvider services,
        Stream responseBody,
        ToolResponseFeature responseFeature,
        CancellationToken requestAborted)
    {
        var headers = new HeaderDictionary();
        foreach (var header in caller.Request.Headers)
        {
            if (!StrippedRequestHeaders.Contains(header.Key))
                headers[header.Key] = header.Value;
        }
        headers["Accept"] = "application/json, text/plain;q=0.9, */*;q=0.8";
        foreach (var (name, value) in bound.Headers)
            headers[name] = value;

        Stream requestBody = Stream.Null;
        if (bound.Body is not null)
        {
            requestBody = new MemoryStream(bound.Body, writable: false);
            headers["Content-Type"] = "application/json; charset=utf-8";
            headers["Content-Length"] = bound.Body.Length.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        var pathBase = caller.Request.PathBase;
        var features = new FeatureCollection();
        features.Set<IHttpRequestFeature>(new HttpRequestFeature
        {
            Protocol = caller.Request.Protocol,
            Scheme = caller.Request.Scheme,
            Method = tool.HttpMethod,
            PathBase = pathBase.Value ?? string.Empty,
            Path = bound.Path,
            QueryString = bound.Query.Value ?? string.Empty,
            RawTarget = pathBase.ToUriComponent() + bound.EscapedPath + bound.Query.ToUriComponent(),
            Headers = headers,
            Body = requestBody,
        });
        // Minimal APIs (.NET 7+) only read a body when the server says the request can have one.
        features.Set<IHttpRequestBodyDetectionFeature>(new RequestBodyDetectionFeature(bound.Body is not null));
        features.Set<IHttpResponseFeature>(responseFeature);
        features.Set<IHttpResponseBodyFeature>(new ToolResponseBodyFeature(responseBody, responseFeature));
        features.Set<IHttpRequestLifetimeFeature>(new HttpRequestLifetimeFeature { RequestAborted = requestAborted });
        features.Set<IHttpRequestIdentifierFeature>(new HttpRequestIdentifierFeature { TraceIdentifier = caller.TraceIdentifier });

        var connection = caller.Connection;
        features.Set<IHttpConnectionFeature>(new HttpConnectionFeature
        {
            ConnectionId = connection.Id,
            RemoteIpAddress = connection.RemoteIpAddress,
            RemotePort = connection.RemotePort,
            LocalIpAddress = connection.LocalIpAddress,
            LocalPort = connection.LocalPort,
        });
        if (caller.Features.Get<ITlsConnectionFeature>() is { } tls)
            features.Set(tls);

        var context = new DefaultHttpContext(features)
        {
            User = caller.User,
            RequestServices = services,
        };
        context.Request.RouteValues = bound.RouteValues;
        context.SetEndpoint(tool.Endpoint);
        return context;
    }

    private static bool SatisfiesRouteConstraints(HttpContext context, RoutePattern pattern, RouteValueDictionary values, out string? error)
    {
        error = null;
        if (pattern.ParameterPolicies.Count == 0)
            return true;

        var factory = context.RequestServices.GetService<ParameterPolicyFactory>();
        if (factory is null)
            return true;

        foreach (var (parameterName, references) in pattern.ParameterPolicies)
        {
            var parameter = pattern.GetParameter(parameterName);
            var optional = parameter is not null && (parameter.IsOptional || parameter.Default is not null);
            if (optional && !values.ContainsKey(parameterName))
                continue;

            foreach (var reference in references)
            {
                if (factory.Create(parameter, reference) is IRouteConstraint constraint
                    && !constraint.Match(context, null, parameterName, values, RouteDirection.IncomingRequest))
                {
                    error = $"'{parameterName}' does not satisfy the route constraint '{reference.Content ?? constraint.GetType().Name}'.";
                    return false;
                }
            }
        }
        return true;
    }

    private static bool TryBindArguments(
        AiToolBinding tool,
        IReadOnlyDictionary<string, JsonElement> arguments,
        out BoundArguments bound,
        out string? error)
    {
        bound = default!;
        error = null;
        var lookup = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in arguments)
            lookup[key] = value;

        var routeValues = new RouteValueDictionary();
        var query = new QueryBuilder();
        var headers = new List<(string, string)>();

        foreach (var parameter in tool.Parameters)
        {
            if (!lookup.TryGetValue(parameter.Name, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                if (parameter.IsRequired)
                {
                    error = $"'{parameter.Name}' is required.";
                    return false;
                }
                continue;
            }

            switch (parameter.Source)
            {
                case AiParameterSource.Path:
                    if (!TryGetScalar(value, out var pathValue) || !IsSafeRouteValue(pathValue, parameter.IsCatchAll))
                    {
                        error = $"'{parameter.Name}' must be a single path value (no '/', '\\', '.' or '..' segments).";
                        return false;
                    }
                    if (pathValue.Length == 0 && parameter.IsRequired)
                    {
                        error = $"'{parameter.Name}' is required.";
                        return false;
                    }
                    routeValues[parameter.Name] = pathValue;
                    break;

                case AiParameterSource.Query:
                    if (value.ValueKind == JsonValueKind.Array)
                    {
                        var items = new List<string>();
                        foreach (var item in value.EnumerateArray())
                        {
                            if (!TryGetScalar(item, out var itemValue))
                            {
                                error = $"'{parameter.Name}' must be an array of scalar values.";
                                return false;
                            }
                            items.Add(itemValue);
                        }
                        query.Add(parameter.Name, items);
                    }
                    else if (TryGetScalar(value, out var queryValue))
                    {
                        query.Add(parameter.Name, queryValue);
                    }
                    else
                    {
                        error = $"'{parameter.Name}' must be a scalar value.";
                        return false;
                    }
                    break;

                case AiParameterSource.Header:
                    if (!TryGetScalar(value, out var headerValue) || headerValue.Any(c => c < 0x20 || c == 0x7f))
                    {
                        error = $"'{parameter.Name}' must be a single-line scalar value.";
                        return false;
                    }
                    headers.Add((parameter.Name, headerValue));
                    break;
            }
        }

        byte[]? body = null;
        if (tool.BodyMode == AiBodyMode.Flattened)
        {
            var bodyObject = new JsonObject();
            foreach (var name in tool.BodyPropertyNames)
            {
                if (lookup.TryGetValue(name, out var value) && value.ValueKind != JsonValueKind.Undefined)
                    bodyObject[name] = JsonNode.Parse(value.GetRawText());
            }
            body = Encoding.UTF8.GetBytes(bodyObject.ToJsonString());
        }
        else if (tool.BodyMode == AiBodyMode.Nested)
        {
            if (lookup.TryGetValue(tool.BodyArgumentName!, out var value) && value.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null))
            {
                body = Encoding.UTF8.GetBytes(value.GetRawText());
            }
            else if (tool.BodyRequired)
            {
                error = $"'{tool.BodyArgumentName}' is required.";
                return false;
            }
        }

        var pattern = tool.Endpoint.RoutePattern;
        foreach (var (key, value) in pattern.Defaults)
        {
            if (!routeValues.ContainsKey(key) && value is not null)
                routeValues[key] = value;
        }
        foreach (var (key, value) in pattern.RequiredValues)
        {
            if (!routeValues.ContainsKey(key) && value is string s)
                routeValues[key] = s;
        }

        var (path, escapedPath) = BuildPath(pattern, routeValues);
        bound = new BoundArguments(routeValues, path, escapedPath, query.ToQueryString(), headers, body);
        return true;
    }

    private static bool TryGetScalar(JsonElement value, out string result)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                result = value.GetString() ?? string.Empty;
                return true;
            case JsonValueKind.Number:
                result = value.GetRawText();
                return true;
            case JsonValueKind.True:
                result = "true";
                return true;
            case JsonValueKind.False:
                result = "false";
                return true;
            default:
                result = string.Empty;
                return false;
        }
    }

    /// <summary>
    /// Route values reach the endpoint exactly as an HTTP request could deliver them: a normal parameter is one
    /// path segment (no separators, never "." or ".."), a catch-all is a sequence of such segments.
    /// </summary>
    internal static bool IsSafeRouteValue(string value, bool isCatchAll)
    {
        if (value.Any(c => c < 0x20 || c == 0x7f || c == '\\'))
            return false;

        if (!isCatchAll)
            return value.IndexOf('/') < 0 && value != "." && value != "..";

        foreach (var segment in value.Split('/'))
        {
            if (segment is "." or "..")
                return false;
        }
        return true;
    }

    private static (string Path, string EscapedPath) BuildPath(RoutePattern pattern, RouteValueDictionary values)
    {
        var path = new StringBuilder();
        var escaped = new StringBuilder();
        foreach (var segment in pattern.PathSegments)
        {
            var segmentText = new StringBuilder();
            var segmentEscaped = new StringBuilder();
            var parts = segment.Parts;
            for (var i = 0; i < parts.Count; i++)
            {
                var part = parts[i];
                // A separator belongs to the optional parameter after it ("{name}.{ext?}"): drop both when it is empty.
                if (part is RoutePatternSeparatorPart
                    && i + 1 < parts.Count
                    && parts[i + 1] is RoutePatternParameterPart { IsOptional: true } nextParameter
                    && string.IsNullOrEmpty(Convert.ToString(values[nextParameter.Name], System.Globalization.CultureInfo.InvariantCulture)))
                {
                    continue;
                }

                switch (part)
                {
                    case RoutePatternLiteralPart literal:
                        segmentText.Append(literal.Content);
                        segmentEscaped.Append(Uri.EscapeDataString(literal.Content));
                        break;
                    case RoutePatternSeparatorPart separator:
                        segmentText.Append(separator.Content);
                        segmentEscaped.Append(Uri.EscapeDataString(separator.Content));
                        break;
                    case RoutePatternParameterPart parameter:
                        var raw = Convert.ToString(values[parameter.Name], System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
                        segmentText.Append(raw);
                        segmentEscaped.Append(parameter.IsCatchAll
                            ? string.Join("/", raw.Split('/').Select(Uri.EscapeDataString))
                            : Uri.EscapeDataString(raw));
                        break;
                }
            }

            if (segmentText.Length == 0)
                continue; // omitted optional parameter
            path.Append('/').Append(segmentText);
            escaped.Append('/').Append(segmentEscaped);
        }

        return path.Length == 0 ? ("/", "/") : (path.ToString(), escaped.ToString());
    }

    private sealed class RequestBodyDetectionFeature : IHttpRequestBodyDetectionFeature
    {
        public RequestBodyDetectionFeature(bool canHaveBody) => CanHaveBody = canHaveBody;

        public bool CanHaveBody { get; }
    }

    private sealed record BoundArguments(
        RouteValueDictionary RouteValues,
        string Path,
        string EscapedPath,
        QueryString Query,
        IReadOnlyList<(string Name, string Value)> Headers,
        byte[]? Body);
}
