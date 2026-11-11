using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HostFiltering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;
using Romatech.Extensions.Ai.Mcp.Configuration;
using Romatech.Extensions.Ai.Mcp.Protocol;
using Romatech.Extensions.Ai.Mcp.Security;
using Romatech.Extensions.Ai.Mcp.Services;

namespace Romatech.Extensions.Ai.Mcp.Http;

/// <summary>
/// HTTP front door of the MCP endpoint (Streamable HTTP transport, JSON responses, stateless).
/// Enforces, in order: method, Origin, authentication/authorization, Content-Type, protocol version, body size and
/// JSON-RPC shape. Shared by <c>app.UseMcp()</c> and <c>app.MapMcp()</c>.
/// </summary>
public sealed class McpHttpHandler
{
    /// <summary>Protocol revision that allows JSON-RPC batches (removed again in 2025-06-18).</summary>
    private const string BatchProtocolVersion = "2025-03-26";
    private const int MaxBatchSize = 20;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly McpRequestHandler _handler;
    private readonly McpOptions _options;
    private readonly ILogger<McpHttpHandler> _logger;
    private readonly IReadOnlyList<string> _filteredHosts;
    private int _warnedNoAuth;

    /// <summary>
    /// Initializes a new instance of the <see cref="McpHttpHandler"/> class.
    /// </summary>
    public McpHttpHandler(McpRequestHandler handler, IOptions<McpOptions> options, ILogger<McpHttpHandler> logger, IServiceProvider services)
    {
        _handler = handler;
        _options = options.Value;
        _logger = logger;
        _filteredHosts = services.GetService<IOptions<HostFilteringOptions>>()?.Value.AllowedHosts?
            .Where(h => !string.IsNullOrWhiteSpace(h) && h != "*")
            .ToList() ?? new List<string>();
    }

    /// <summary>
    /// Handles one HTTP request addressed to the MCP endpoint.
    /// </summary>
    public async Task HandleAsync(HttpContext context)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));
        var request = context.Request;
        var response = context.Response;
        response.Headers[HeaderNames.CacheControl] = "no-store";

        if (!HttpMethods.IsPost(request.Method))
        {
            // Stateless JSON-only server: no SSE stream (GET) and no sessions (DELETE).
            response.StatusCode = StatusCodes.Status405MethodNotAllowed;
            response.Headers[HeaderNames.Allow] = "POST";
            return;
        }

        if (!IsOriginAllowed(context))
        {
            _logger.LogWarning("MCP request rejected: Origin {Origin} is not allowed", request.Headers[HeaderNames.Origin].ToString());
            await WriteErrorAsync(context, StatusCodes.Status403Forbidden, McpErrorCodes.InvalidRequest, "Origin not allowed").ConfigureAwait(false);
            return;
        }

        var outcome = await McpAuthorization.AuthorizeMcpCallerAsync(context, _options.RequireAuthentication, _options.AuthorizationPolicy).ConfigureAwait(false);
        if (outcome == AuthorizationOutcome.Unauthenticated)
        {
            if (Interlocked.Exchange(ref _warnedNoAuth, 1) == 0)
            {
                _logger.LogInformation(
                    "MCP request rejected because the caller is not authenticated. The MCP endpoint requires authentication by default; " +
                    "authenticate the client with the application's scheme or set McpOptions.RequireAuthentication = false deliberately.");
            }
            await ChallengeAsync(context).ConfigureAwait(false);
            if (response.HasStarted)
                return; // the scheme wrote its own challenge response
            await WriteErrorAsync(context, StatusCodes.Status401Unauthorized, McpErrorCodes.InvalidRequest, "Unauthorized").ConfigureAwait(false);
            return;
        }
        if (outcome == AuthorizationOutcome.Forbidden)
        {
            await WriteErrorAsync(context, StatusCodes.Status403Forbidden, McpErrorCodes.InvalidRequest, "Forbidden").ConfigureAwait(false);
            return;
        }

        if (!IsJsonContentType(request.ContentType))
        {
            // Also blocks "simple" cross-site requests (text/plain, form posts) that skip CORS preflight.
            await WriteErrorAsync(context, StatusCodes.Status415UnsupportedMediaType, McpErrorCodes.InvalidRequest, "Content-Type must be application/json").ConfigureAwait(false);
            return;
        }

        var protocolVersion = request.Headers["MCP-Protocol-Version"].ToString();
        if (protocolVersion.Length > 0 && !McpRequestHandler.SupportedProtocolVersions.Contains(protocolVersion))
        {
            await WriteErrorAsync(context, StatusCodes.Status400BadRequest, McpErrorCodes.InvalidRequest, $"Unsupported MCP-Protocol-Version: {protocolVersion}").ConfigureAwait(false);
            return;
        }

        if (request.ContentLength > _options.MaxRequestBodyBytes)
        {
            await WriteErrorAsync(context, StatusCodes.Status413PayloadTooLarge, McpErrorCodes.InvalidRequest, "Request body too large").ConfigureAwait(false);
            return;
        }

        JsonDocument document;
        try
        {
            var body = await ReadBodyAsync(context, _options.MaxRequestBodyBytes).ConfigureAwait(false);
            if (body is null)
            {
                await WriteErrorAsync(context, StatusCodes.Status413PayloadTooLarge, McpErrorCodes.InvalidRequest, "Request body too large").ConfigureAwait(false);
                return;
            }
            document = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 64 });
        }
        catch (JsonException)
        {
            await WriteErrorAsync(context, StatusCodes.Status400BadRequest, McpErrorCodes.ParseError, "Parse error").ConfigureAwait(false);
            return;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Array)
            {
                await HandleBatchAsync(context, root, protocolVersion).ConfigureAwait(false);
                return;
            }

            var (reply, status) = await ProcessMessageAsync(context, root, allowInitialize: true).ConfigureAwait(false);
            if (reply is null)
            {
                response.StatusCode = StatusCodes.Status202Accepted;
                return;
            }

            await WriteJsonAsync(context, status, reply).ConfigureAwait(false);
        }
    }

    private async Task HandleBatchAsync(HttpContext context, JsonElement root, string protocolVersion)
    {
        // Batches exist only in protocol revision 2025-03-26 (a client of that revision may omit the header).
        var effectiveVersion = protocolVersion.Length > 0 ? protocolVersion : BatchProtocolVersion;
        if (effectiveVersion != BatchProtocolVersion)
        {
            await WriteErrorAsync(context, StatusCodes.Status400BadRequest, McpErrorCodes.InvalidRequest, "Batch requests are not supported in this protocol version").ConfigureAwait(false);
            return;
        }

        var count = root.GetArrayLength();
        if (count == 0 || count > MaxBatchSize)
        {
            await WriteErrorAsync(context, StatusCodes.Status400BadRequest, McpErrorCodes.InvalidRequest, $"A batch must contain between 1 and {MaxBatchSize} messages").ConfigureAwait(false);
            return;
        }

        var replies = new List<McpResponse>();
        foreach (var element in root.EnumerateArray())
        {
            var (reply, _) = await ProcessMessageAsync(context, element, allowInitialize: false).ConfigureAwait(false);
            if (reply is not null)
                replies.Add(reply);
        }

        if (replies.Count == 0)
        {
            context.Response.StatusCode = StatusCodes.Status202Accepted;
            return;
        }
        await WriteJsonAsync(context, StatusCodes.Status200OK, replies).ConfigureAwait(false);
    }

    /// <summary>
    /// Validates one JSON-RPC message and dispatches it. Returns <c>null</c> for notifications.
    /// </summary>
    private async Task<(McpResponse? Reply, int Status)> ProcessMessageAsync(HttpContext context, JsonElement root, bool allowInitialize)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return (McpResponse.Failure(null, McpErrorCodes.InvalidRequest, "Request must be a JSON object"), StatusCodes.Status400BadRequest);

        JsonElement? id = null;
        if (root.TryGetProperty("id", out var idElement))
        {
            if (idElement.ValueKind is not (JsonValueKind.String or JsonValueKind.Number))
                return (McpResponse.Failure(null, McpErrorCodes.InvalidRequest, "id must be a string or a number"), StatusCodes.Status400BadRequest);
            id = idElement.Clone();
        }

        if (!root.TryGetProperty("jsonrpc", out var version) || version.ValueKind != JsonValueKind.String || version.GetString() != "2.0")
            return (McpResponse.Failure(id, McpErrorCodes.InvalidRequest, "jsonrpc must be \"2.0\""), StatusCodes.Status400BadRequest);

        if (!root.TryGetProperty("method", out var methodElement) || methodElement.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(methodElement.GetString()))
            return (McpResponse.Failure(id, McpErrorCodes.InvalidRequest, "method is required"), StatusCodes.Status400BadRequest);

        var method = methodElement.GetString()!;
        if (!allowInitialize && method == "initialize")
            return (McpResponse.Failure(id, McpErrorCodes.InvalidRequest, "initialize cannot be part of a batch"), StatusCodes.Status400BadRequest);

        // The envelope is valid from here on: problems with params are "Invalid params" (-32602) on HTTP 200.
        McpRequestParams? parameters = null;
        if (root.TryGetProperty("params", out var paramsElement) && paramsElement.ValueKind != JsonValueKind.Null)
        {
            string? paramsError = null;
            if (paramsElement.ValueKind != JsonValueKind.Object)
            {
                paramsError = "params must be an object";
            }
            else
            {
                try
                {
                    parameters = paramsElement.Deserialize<McpRequestParams>(JsonOptions);
                }
                catch (JsonException)
                {
                    paramsError = "params has an invalid shape";
                }
            }

            if (paramsError is not null)
            {
                return id is null
                    ? (null, StatusCodes.Status202Accepted)
                    : (McpResponse.Failure(id, McpErrorCodes.InvalidParams, $"Invalid params: {paramsError}"), StatusCodes.Status200OK);
            }

            // Detach from the pooled document, which is disposed after the response is written.
            if (parameters?.Arguments is { } args)
                parameters.Arguments = args.ToDictionary(kv => kv.Key, kv => kv.Value.Clone(), StringComparer.Ordinal);
        }

        var mcpRequest = new McpRequest { JsonRpc = "2.0", Id = id, Method = method, Params = parameters };
        var reply = await _handler.HandleAsync(mcpRequest, context, context.RequestAborted).ConfigureAwait(false);
        return (reply, StatusCodes.Status200OK);
    }

    private bool IsOriginAllowed(HttpContext context)
    {
        var origins = context.Request.Headers[HeaderNames.Origin];
        if (origins.Count == 0)
            return true; // non-browser client
        if (origins.Count > 1)
            return false;

        var origin = origins.ToString();
        if (_options.AllowedOrigins.Any(o => string.Equals(o.TrimEnd('/'), origin, StringComparison.OrdinalIgnoreCase)))
            return true;

        var request = context.Request;
        var self = $"{request.Scheme}://{request.Host.Value}";
        if (!string.Equals(origin, self, StringComparison.OrdinalIgnoreCase))
            return false;

        // Same origin. The Host header is chosen by the browser from the page's URL, so a DNS-rebinding page can make
        // Origin and Host agree. That only matters when the endpoint is reachable without credentials (cookies and
        // tokens are bound to the real host), so anonymous endpoints additionally require a trusted host.
        var anonymous = !_options.RequireAuthentication && string.IsNullOrWhiteSpace(_options.AuthorizationPolicy);
        return !anonymous || IsTrustedHost(request.Host.Host);
    }

    private bool IsTrustedHost(string host)
    {
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
            || (IPAddress.TryParse(host.Trim('[', ']'), out var ip) && IPAddress.IsLoopback(ip)))
            return true;

        return _options.AllowedHosts.Concat(_filteredHosts).Any(pattern => HostMatches(pattern, host));
    }

    private static bool HostMatches(string pattern, string host)
    {
        pattern = pattern.Trim();
        if (pattern.StartsWith("*.", StringComparison.Ordinal))
            return host.EndsWith(pattern.Substring(1), StringComparison.OrdinalIgnoreCase) && host.Length > pattern.Length - 1;
        return string.Equals(pattern, host, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task ChallengeAsync(HttpContext context)
    {
        // Let the application's default challenge scheme add its headers (e.g. JwtBearer's WWW-Authenticate),
        // but never redirect an MCP client to a login page.
        var schemes = context.RequestServices.GetService<IAuthenticationSchemeProvider>();
        if (schemes is null || await schemes.GetDefaultChallengeSchemeAsync().ConfigureAwait(false) is null)
            return;

        try
        {
            await context.ChallengeAsync().ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            return;
        }

        if (!context.Response.HasStarted)
            context.Response.Headers.Remove(HeaderNames.Location);
    }

    private static bool IsJsonContentType(string? contentType)
    {
        if (!MediaTypeHeaderValue.TryParse(contentType, out var mediaType))
            return false;
        if (!mediaType.MediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase))
            return false;
        var charset = HeaderUtilities.RemoveQuotes(mediaType.Charset);
        return StringSegment.IsNullOrEmpty(charset) || charset.Equals("utf-8", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<byte[]?> ReadBodyAsync(HttpContext context, long limit)
    {
        if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } sizeFeature
            && (sizeFeature.MaxRequestBodySize is null || sizeFeature.MaxRequestBodySize > limit))
        {
            sizeFeature.MaxRequestBodySize = limit;
        }

        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await context.Request.Body.ReadAsync(chunk.AsMemory(), context.RequestAborted).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > limit)
                return null;
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    private static async Task WriteJsonAsync<T>(HttpContext context, int statusCode, T payload)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";
        await JsonSerializer.SerializeAsync(context.Response.Body, payload, JsonOptions, context.RequestAborted).ConfigureAwait(false);
    }

    private static Task WriteErrorAsync(HttpContext context, int statusCode, int code, string message, JsonElement? id = null)
        => WriteJsonAsync(context, statusCode, McpResponse.Failure(id, code, message));
}
