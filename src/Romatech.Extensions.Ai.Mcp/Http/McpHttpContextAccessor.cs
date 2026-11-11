using Microsoft.AspNetCore.Http;

namespace Romatech.Extensions.Ai.Mcp.Http;

/// <summary>
/// Decorates the application's <see cref="IHttpContextAccessor"/> so that, while an MCP tool runs, code that reads
/// <c>IHttpContextAccessor.HttpContext</c> sees the tool's own request (its route values, query, body and request
/// services) instead of the outer MCP request. Outside tool calls it behaves exactly like the decorated accessor.
/// Registered automatically by <c>services.UseMcp()</c>.
/// </summary>
public sealed class McpHttpContextAccessor : IHttpContextAccessor
{
    private static readonly AsyncLocal<HttpContext?> ToolContext = new();
    private readonly IHttpContextAccessor _inner;

    /// <summary>
    /// Initializes a new instance of the <see cref="McpHttpContextAccessor"/> class.
    /// </summary>
    public McpHttpContextAccessor(IHttpContextAccessor inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    /// <inheritdoc />
    public HttpContext? HttpContext
    {
        get => ToolContext.Value ?? _inner.HttpContext;
        set => _inner.HttpContext = value;
    }

    /// <summary>
    /// Makes <paramref name="context"/> the current context for the calling async flow.
    /// Must be called from inside a dedicated async method so the change does not leak to the caller.
    /// </summary>
    internal static void EnterToolContext(HttpContext context) => ToolContext.Value = context;
}
