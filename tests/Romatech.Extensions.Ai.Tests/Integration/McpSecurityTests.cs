using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Romatech.Extensions.Ai.Tests.Integration;

public sealed class CrmFixture : IAsyncLifetime
{
    public TestHost Host { get; private set; } = default!;

    public async Task InitializeAsync()
    {
        Host = await TestHost.StartAsync(
            b => CrmApp.ConfigureServices(b, o =>
            {
                o.AllowedOrigins.Add("https://trusted.example");
                o.MaxToolResponseBytes = 1024;
                o.ToolTimeout = TimeSpan.FromSeconds(1);
            }),
            app => CrmApp.Configure(app));
    }

    public async Task DisposeAsync() => await Host.DisposeAsync();

    public async Task<McpTestClient> LoggedInAsync(string user, params string[] roles)
    {
        var client = Host.CreateClient();
        await client.LoginAsync(user, roles);
        return client;
    }
}

/// <summary>
/// Regression tests for every failure reported against 3.0.0, run against a real Kestrel server.
/// </summary>
public sealed class McpSecurityTests : IClassFixture<CrmFixture>
{
    private readonly CrmFixture _fixture;

    public McpSecurityTests(CrmFixture fixture) => _fixture = fixture;

    // 3.0.0: every call to /mcp returned 500.
    [Fact]
    public async Task Initialize_succeeds_for_an_authenticated_caller()
    {
        using var client = await _fixture.LoggedInAsync("alice");

        var reply = await client.RpcAsync("initialize", new { protocolVersion = "2025-06-18", capabilities = new { }, clientInfo = new { name = "t", version = "1" } });

        reply.Status.Should().Be(HttpStatusCode.OK, reply.Raw);
        reply.Result.GetProperty("protocolVersion").GetString().Should().Be("2025-06-18");
        reply.Result.GetProperty("capabilities").TryGetProperty("tools", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Initialize_falls_back_to_latest_supported_version()
    {
        using var client = await _fixture.LoggedInAsync("alice");

        var reply = await client.RpcAsync("initialize", new { protocolVersion = "1999-01-01" });

        reply.Result.GetProperty("protocolVersion").GetString().Should().Be("2025-11-25");
    }

    // 3.0.0: /mcp was open without login.
    [Fact]
    public async Task Anonymous_caller_is_rejected_by_default()
    {
        using var client = _fixture.Host.CreateClient();

        var reply = await client.RpcAsync("tools/list");

        reply.Status.Should().Be(HttpStatusCode.Unauthorized);
        reply.Raw.Should().NotContain("get_customer");
    }

    [Fact]
    public async Task Anonymous_caller_cannot_call_tools()
    {
        using var client = _fixture.Host.CreateClient();

        var reply = await client.CallToolAsync("open_tool");

        reply.Status.Should().Be(HttpStatusCode.Unauthorized);
    }

    // 3.0.0: /mcp accepted calls coming from other sites.
    [Fact]
    public async Task Cross_site_origin_is_rejected()
    {
        using var client = await _fixture.LoggedInAsync("alice");

        var reply = await client.RpcAsync("tools/list", configure: r => r.Headers.Add("Origin", "https://evil.example"));

        reply.Status.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Same_origin_and_allow_listed_origins_are_accepted()
    {
        using var client = await _fixture.LoggedInAsync("alice");
        var self = _fixture.Host.BaseAddress.GetLeftPart(UriPartial.Authority);

        (await client.RpcAsync("ping", configure: r => r.Headers.Add("Origin", self))).Status.Should().Be(HttpStatusCode.OK);
        (await client.RpcAsync("ping", configure: r => r.Headers.Add("Origin", "https://trusted.example"))).Status.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("text/plain")]
    [InlineData("application/x-www-form-urlencoded")]
    [InlineData("multipart/form-data; boundary=x")]
    public async Task Simple_cross_site_content_types_are_rejected(string contentType)
    {
        using var client = await _fixture.LoggedInAsync("alice");

        var reply = await client.SendRawAsync("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"delete_customer","arguments":{"id":"1"}}}""", contentType);

        reply.Status.Should().Be(HttpStatusCode.UnsupportedMediaType);
    }

    // 3.0.0: tools did not run with the caller's login (every call was 401, reported as success).
    [Fact]
    public async Task Tool_runs_with_the_callers_identity_host_and_headers()
    {
        using var client = await _fixture.LoggedInAsync("alice");

        var reply = await client.CallToolAsync("get_customer", new { id = "42" }, r => r.Headers.Add("X-Tenant", "acme"));

        reply.IsToolError.Should().BeFalse(reply.ToolText);
        using var body = JsonDocument.Parse(reply.ToolText);
        body.RootElement.GetProperty("id").GetString().Should().Be("42");
        body.RootElement.GetProperty("requestedBy").GetString().Should().Be("alice");
        body.RootElement.GetProperty("host").GetString().Should().Be(_fixture.Host.BaseAddress.Authority);
        body.RootElement.GetProperty("tenant").GetString().Should().Be("acme");
        body.RootElement.GetProperty("path").GetString().Should().Be("/api/customers/42");
    }

    [Fact]
    public async Task Mvc_tool_runs_with_the_callers_identity_and_flattened_body()
    {
        using var client = await _fixture.LoggedInAsync("alice");

        var reply = await client.CallToolAsync("create_order", new { productId = "sku-1", quantity = 3 });

        reply.IsToolError.Should().BeFalse(reply.ToolText);
        using var body = JsonDocument.Parse(reply.ToolText);
        body.RootElement.GetProperty("productId").GetString().Should().Be("sku-1");
        body.RootElement.GetProperty("quantity").GetInt32().Should().Be(3);
        body.RootElement.GetProperty("user").GetString().Should().Be("alice");
    }

    [Fact]
    public async Task Minimal_api_body_tool_returns_created()
    {
        using var client = await _fixture.LoggedInAsync("alice");

        var reply = await client.CallToolAsync("create_customer", new { name = "Ana", email = "ana@example.com" });

        reply.IsToolError.Should().BeFalse(reply.ToolText);
        reply.ToolText.Should().Contain("\"createdBy\":\"alice\"");
    }

    // 3.0.0: "../../leads" as an argument reached a route marked hidden and returned its data.
    [Theory]
    [InlineData("../leads")]
    [InlineData("../../api/leads")]
    [InlineData("..")]
    [InlineData("42/../../leads")]
    [InlineData("..\\leads")]
    public async Task Path_traversal_cannot_reach_a_hidden_route(string id)
    {
        using var client = await _fixture.LoggedInAsync("alice");

        var reply = await client.CallToolAsync("get_customer", new { id });

        reply.IsToolError.Should().BeTrue();
        reply.Raw.Should().NotContain(CrmApp.LeadsSecret);
    }

    [Fact]
    public async Task Encoded_traversal_is_treated_as_data_not_as_a_path()
    {
        using var client = await _fixture.LoggedInAsync("alice");

        var reply = await client.CallToolAsync("get_customer", new { id = "..%2F..%2Fleads" });

        reply.Raw.Should().NotContain(CrmApp.LeadsSecret);
        using var body = JsonDocument.Parse(reply.ToolText);
        body.RootElement.GetProperty("id").GetString().Should().Be("..%2F..%2Fleads");
    }

    [Fact]
    public async Task Hidden_routes_are_not_tools()
    {
        using var client = await _fixture.LoggedInAsync("alice");

        var list = await client.RpcAsync("tools/list");
        list.ToolNames.Should().NotContain(n => n.Contains("leads") || n.Contains("delete_order") || n.Contains("login"));

        var call = await client.CallToolAsync("get_api_leads");
        call.HasError.Should().BeTrue();
        call.Raw.Should().NotContain(CrmApp.LeadsSecret);
    }

    [Fact]
    public async Task Catch_all_route_accepts_segments_but_not_dot_segments()
    {
        using var client = await _fixture.LoggedInAsync("alice");

        var ok = await client.CallToolAsync("read_file", new { path = "docs/readme.md" });
        ok.IsToolError.Should().BeFalse(ok.ToolText);
        ok.ToolText.Should().Contain("docs/readme.md");

        var bad = await client.CallToolAsync("read_file", new { path = "docs/../../etc/passwd" });
        bad.IsToolError.Should().BeTrue();
    }

    [Fact]
    public async Task Route_constraints_are_enforced()
    {
        using var client = await _fixture.LoggedInAsync("alice");

        var bad = await client.CallToolAsync("customer_orders", new { id = "abc" });
        bad.IsToolError.Should().BeTrue();
        bad.ToolText.Should().Contain("route constraint");

        var ok = await client.CallToolAsync("customer_orders", new { id = 7 });
        ok.IsToolError.Should().BeFalse(ok.ToolText);
        ok.ToolText.Should().Contain("70");
    }

    [Fact]
    public async Task Query_values_are_encoded_and_cannot_inject_parameters()
    {
        using var client = await _fixture.LoggedInAsync("alice");

        var reply = await client.CallToolAsync("search", new { q = "a b&limit=999#x", limit = 5 });

        reply.IsToolError.Should().BeFalse(reply.ToolText);
        using var body = JsonDocument.Parse(reply.ToolText);
        body.RootElement.GetProperty("q").GetString().Should().Be("a b&limit=999#x");
        body.RootElement.GetProperty("limit").GetInt32().Should().Be(5);
    }

    // Endpoint authorization is evaluated exactly as AuthorizationMiddleware would.
    [Fact]
    public async Task Endpoint_policies_are_enforced_per_caller()
    {
        using var sales = await _fixture.LoggedInAsync("bob", "sales");
        (await sales.RpcAsync("tools/list")).ToolNames.Should().NotContain("admin_report", "tools the endpoint policy denies are hidden");
        var denied = await sales.CallToolAsync("admin_report");
        denied.IsToolError.Should().BeTrue();
        denied.ToolText.Should().Contain("403");
        denied.ToolText.Should().NotContain("admin-only");

        using var admin = await _fixture.LoggedInAsync("carol", "admin");
        (await admin.RpcAsync("tools/list")).ToolNames.Should().Contain("admin_report");
        var allowed = await admin.CallToolAsync("admin_report");
        allowed.IsToolError.Should().BeFalse(allowed.ToolText);
        allowed.ToolText.Should().Contain("admin-only");
    }

    [Fact]
    public async Task AiRole_hides_and_blocks_tools_for_other_roles()
    {
        using var sales = await _fixture.LoggedInAsync("bob", "sales");
        (await sales.RpcAsync("tools/list")).ToolNames.Should().NotContain("finance_summary");
        var call = await sales.CallToolAsync("finance_summary");
        call.HasError.Should().BeTrue();
        call.Raw.Should().NotContain("42");

        using var finance = await _fixture.LoggedInAsync("dave", "finance");
        (await finance.RpcAsync("tools/list")).ToolNames.Should().Contain("finance_summary");
        (await finance.CallToolAsync("finance_summary")).IsToolError.Should().BeFalse();
    }

    // 3.0.0: an HTTP error from the endpoint was reported as a successful tool call.
    [Fact]
    public async Task Non_success_status_is_reported_as_tool_error()
    {
        using var client = await _fixture.LoggedInAsync("alice");

        var reply = await client.CallToolAsync("missing");

        reply.IsToolError.Should().BeTrue();
        reply.ToolText.Should().StartWith("HTTP 404");
        reply.ToolText.Should().Contain("customer not found");
    }

    [Fact]
    public async Task No_content_is_success()
    {
        using var client = await _fixture.LoggedInAsync("alice");

        var reply = await client.CallToolAsync("delete_customer", new { id = "9" });

        reply.IsToolError.Should().BeFalse(reply.ToolText);
        reply.ToolText.Should().StartWith("HTTP 204");
    }

    [Fact]
    public async Task Exceptions_do_not_leak_details()
    {
        using var client = await _fixture.LoggedInAsync("alice");

        var reply = await client.CallToolAsync("boom");

        reply.IsToolError.Should().BeTrue();
        reply.ToolText.Should().StartWith("HTTP 500");
        reply.Raw.Should().NotContain("hunter2").And.NotContain("InvalidOperationException");
    }

    [Fact]
    public async Task Declared_header_parameters_are_sent()
    {
        using var client = await _fixture.LoggedInAsync("alice");

        var schema = (await client.RpcAsync("tools/list")).Tool("region").GetProperty("inputSchema");
        schema.GetProperty("required")[0].GetString().Should().Be("X-Region", "the argument uses the documented header name");

        var reply = await client.CallToolAsync("region", new Dictionary<string, object> { ["X-Region"] = "sa-east" });

        reply.IsToolError.Should().BeFalse(reply.ToolText);
        reply.ToolText.Should().Contain("sa-east");
    }

    [Fact]
    public async Task Missing_required_argument_is_a_tool_error()
    {
        using var client = await _fixture.LoggedInAsync("alice");

        var reply = await client.CallToolAsync("get_customer", new { });

        reply.IsToolError.Should().BeTrue();
        reply.ToolText.Should().Contain("'id' is required");
    }

    [Fact]
    public async Task Large_responses_are_truncated()
    {
        using var client = await _fixture.LoggedInAsync("alice");

        var reply = await client.CallToolAsync("big");

        reply.ToolText.Should().Contain("[response truncated: 4096 bytes, limit 1024]");
        reply.ToolText.Length.Should().BeLessThan(1200);
    }

    [Fact]
    public async Task Slow_tools_time_out()
    {
        using var client = await _fixture.LoggedInAsync("alice");

        var reply = await client.CallToolAsync("slow");

        reply.IsToolError.Should().BeTrue();
        reply.ToolText.Should().Contain("did not finish");
    }

    [Fact]
    public async Task IHttpContextAccessor_returns_the_tool_request_inside_the_tool()
    {
        using var client = await _fixture.LoggedInAsync("alice");

        for (var i = 0; i < 2; i++)
        {
            var reply = await client.CallToolAsync("accessor", new { id = "77" });
            reply.IsToolError.Should().BeFalse(reply.ToolText);
            using var body = JsonDocument.Parse(reply.ToolText);
            body.RootElement.GetProperty("accessorPath").GetString().Should().Be("/api/accessor/77");
            body.RootElement.GetProperty("accessorRouteId").GetString().Should().Be("77");
        }
    }

    [Fact]
    public async Task OnStarting_and_RegisterForDispose_run_for_tool_calls()
    {
        using var client = await _fixture.LoggedInAsync("alice");
        var probe = _fixture.Host.Services.GetRequiredService<DisposalProbe>();
        var disposedBefore = probe.DisposedCount;
        var startedBefore = probe.StartedCount;

        var reply = await client.CallToolAsync("disposable");

        reply.IsToolError.Should().BeFalse(reply.ToolText);
        probe.StartedCount.Should().Be(startedBefore + 1);
        probe.DisposedCount.Should().Be(disposedBefore + 1);
    }

    [Fact]
    public async Task Timeout_is_enforced_even_when_the_endpoint_ignores_cancellation()
    {
        using var client = await _fixture.LoggedInAsync("alice");
        var watch = System.Diagnostics.Stopwatch.StartNew();

        var reply = await client.CallToolAsync("stubborn");

        watch.Stop();
        reply.IsToolError.Should().BeTrue();
        reply.ToolText.Should().Contain("did not finish");
        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2.5));
    }

    [Fact]
    public async Task Host_restricted_endpoints_are_not_tools()
    {
        using var client = await _fixture.LoggedInAsync("alice");

        (await client.RpcAsync("tools/list")).ToolNames.Should().NotContain("other_host");
        (await client.CallToolAsync("other_host")).HasError.Should().BeTrue();
    }

    [Fact]
    public async Task Optional_complex_segment_is_omitted_with_its_separator()
    {
        using var client = await _fixture.LoggedInAsync("alice");

        var without = await client.CallToolAsync("download", new { name = "report" });
        without.IsToolError.Should().BeFalse(without.ToolText);
        without.ToolText.Should().Contain("\"path\":\"/api/download/report\"");

        var with = await client.CallToolAsync("download", new { name = "report", ext = "pdf" });
        with.ToolText.Should().Contain("\"path\":\"/api/download/report.pdf\"");
    }

    [Fact]
    public async Task Tool_annotations_follow_http_semantics()
    {
        using var client = await _fixture.LoggedInAsync("alice");

        var list = await client.RpcAsync("tools/list");

        list.Tool("get_customer").GetProperty("annotations").GetProperty("readOnlyHint").GetBoolean().Should().BeTrue();
        var delete = list.Tool("delete_customer").GetProperty("annotations");
        delete.GetProperty("readOnlyHint").GetBoolean().Should().BeFalse();
        delete.GetProperty("destructiveHint").GetBoolean().Should().BeTrue();
        list.Tool("create_customer").GetProperty("annotations").TryGetProperty("destructiveHint", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Unauthenticated_caller_gets_401_not_a_login_redirect()
    {
        using var client = _fixture.Host.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent("""{"jsonrpc":"2.0","id":1,"method":"ping"}""", System.Text.Encoding.UTF8, "application/json"),
        };
        using var response = await client.Http.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.Location.Should().BeNull();
    }

    [Fact]
    public async Task Unknown_tool_is_a_protocol_error()
    {
        using var client = await _fixture.LoggedInAsync("alice");

        var reply = await client.CallToolAsync("does_not_exist");

        reply.ErrorCode.Should().Be(-32602);
    }
}
