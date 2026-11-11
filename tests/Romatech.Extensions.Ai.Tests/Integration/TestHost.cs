using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Romatech.Extensions.Ai.Tests.Integration;

/// <summary>
/// A real Kestrel server on a random loopback port. No TestServer: requests go through the real HTTP stack,
/// cookies and all, like the production report that motivated these tests.
/// </summary>
public sealed class TestHost : IAsyncDisposable
{
    private readonly WebApplication _app;

    private TestHost(WebApplication app, Uri baseAddress)
    {
        _app = app;
        BaseAddress = baseAddress;
    }

    public Uri BaseAddress { get; }

    public IServiceProvider Services => _app.Services;

    public static async Task<TestHost> StartAsync(Action<WebApplicationBuilder> configureServices, Action<WebApplication> configureApp)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Production",
            ApplicationName = typeof(TestHost).Assembly.GetName().Name,
        });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        configureServices(builder);

        var app = builder.Build();
        configureApp(app);
        await app.StartAsync();

        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        return new TestHost(app, new Uri(address));
    }

    public McpTestClient CreateClient() => new(BaseAddress);

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}

/// <summary>
/// Minimal MCP-over-HTTP client with a cookie jar.
/// </summary>
public sealed class McpTestClient : IDisposable
{
    private readonly HttpClient _http;
    private int _nextId;

    public McpTestClient(Uri baseAddress)
    {
        var handler = new HttpClientHandler { UseCookies = true, CookieContainer = new CookieContainer(), AllowAutoRedirect = false };
        _http = new HttpClient(handler) { BaseAddress = baseAddress };
    }

    public HttpClient Http => _http;

    public string McpPath { get; set; } = "/mcp";

    public async Task LoginAsync(string user, params string[] roles)
    {
        var query = $"/login?user={Uri.EscapeDataString(user)}" + string.Concat(roles.Select(r => "&role=" + Uri.EscapeDataString(r)));
        var response = await _http.GetAsync(query);
        response.EnsureSuccessStatusCode();
    }

    public Task<McpReply> RpcAsync(string method, object? parameters = null, Action<HttpRequestMessage>? configure = null)
    {
        var payload = new Dictionary<string, object?> { ["jsonrpc"] = "2.0", ["id"] = Interlocked.Increment(ref _nextId), ["method"] = method };
        if (parameters is not null) payload["params"] = parameters;
        return SendRawAsync(JsonSerializer.Serialize(payload), configure: configure);
    }

    public Task<McpReply> CallToolAsync(string name, object? arguments = null, Action<HttpRequestMessage>? configure = null)
        => RpcAsync("tools/call", new { name, arguments = arguments ?? new { } }, configure);

    public async Task<McpReply> SendRawAsync(string body, string contentType = "application/json", Action<HttpRequestMessage>? configure = null, HttpMethod? method = null)
    {
        using var request = new HttpRequestMessage(method ?? HttpMethod.Post, McpPath);
        if (method != HttpMethod.Get)
        {
            request.Content = new ByteArrayContent(Encoding.UTF8.GetBytes(body));
            request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        }
        request.Headers.Accept.ParseAdd("application/json, text/event-stream");
        configure?.Invoke(request);

        using var response = await _http.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        JsonElement? json = null;
        if (text.Length > 0)
        {
            try { json = JsonDocument.Parse(text).RootElement.Clone(); }
            catch (JsonException) { }
        }
        return new McpReply(response.StatusCode, json, text);
    }

    public void Dispose() => _http.Dispose();
}

public sealed record McpReply(HttpStatusCode Status, JsonElement? Json, string Raw)
{
    public JsonElement Result => Json!.Value.GetProperty("result");

    public bool HasError => Json is { } j && j.TryGetProperty("error", out _);

    public int ErrorCode => Json!.Value.GetProperty("error").GetProperty("code").GetInt32();

    public string ErrorMessage => Json!.Value.GetProperty("error").GetProperty("message").GetString()!;

    public bool IsToolError => Result.GetProperty("isError").GetBoolean();

    public string ToolText => Result.GetProperty("content")[0].GetProperty("text").GetString()!;

    public IReadOnlyList<string> ToolNames => Result.GetProperty("tools").EnumerateArray().Select(t => t.GetProperty("name").GetString()!).ToList();

    public JsonElement Tool(string name) => Result.GetProperty("tools").EnumerateArray().Single(t => t.GetProperty("name").GetString() == name);
}
