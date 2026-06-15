using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Romatech.Extensions.Ai.Shared.Models;

namespace Romatech.Extensions.Ai.Mcp.Services;

/// <summary>
/// Executes MCP tool calls by forwarding to the underlying ASP.NET endpoint.
/// </summary>
public sealed class McpToolExecutor
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<McpToolExecutor> _logger;

    public McpToolExecutor(
        IHttpClientFactory httpClientFactory,
        IServiceProvider serviceProvider,
        ILogger<McpToolExecutor> logger)
    {
        _httpClientFactory = httpClientFactory;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <summary>
    /// Executes a tool call against the underlying endpoint.
    /// </summary>
    public async Task<object?> ExecuteAsync(
        AiEndpointDescriptor descriptor,
        Dictionary<string, object>? arguments,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Executing tool {ToolName} at {Route}", descriptor.ToolName, descriptor.Route);

        var client = _httpClientFactory.CreateClient("RomatechAiMcpInternal");

        var route = BuildRoute(descriptor.Route, arguments);
        var requestMessage = new HttpRequestMessage(new HttpMethod(descriptor.HttpMethod), route);

        if (descriptor.HttpMethod is "POST" or "PUT" or "PATCH" && arguments is not null)
        {
            var json = JsonSerializer.Serialize(arguments);
            requestMessage.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        }

        var response = await client.SendAsync(requestMessage, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Tool {ToolName} returned {StatusCode}: {Content}",
                descriptor.ToolName, (int)response.StatusCode, content);
        }

        try
        {
            return JsonSerializer.Deserialize<object>(content);
        }
        catch
        {
            return content;
        }
    }

    private static string BuildRoute(string routeTemplate, Dictionary<string, object>? arguments)
    {
        if (arguments is null) return routeTemplate;

        var route = routeTemplate;
        foreach (var (key, value) in arguments)
        {
            route = route.Replace($"{{{key}}}", value?.ToString() ?? string.Empty);
        }

        return route;
    }
}
