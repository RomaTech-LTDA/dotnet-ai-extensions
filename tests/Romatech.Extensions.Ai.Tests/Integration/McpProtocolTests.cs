using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Romatech.Extensions.Ai.Extensions;
using Romatech.Extensions.Ai.Metadata.Extensions;
using Xunit;

namespace Romatech.Extensions.Ai.Tests.Integration;

public sealed class McpProtocolTests : IClassFixture<CrmFixture>
{
    private readonly CrmFixture _fixture;

    public McpProtocolTests(CrmFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Notifications_get_202_without_body()
    {
        using var client = await _fixture.LoggedInAsync("alice");

        var reply = await client.SendRawAsync("""{"jsonrpc":"2.0","method":"notifications/initialized"}""");

        reply.Status.Should().Be(HttpStatusCode.Accepted);
        reply.Raw.Should().BeEmpty();
    }

    [Fact]
    public async Task Batches_are_rejected_for_protocol_versions_without_batching()
    {
        using var client = await _fixture.LoggedInAsync("alice");

        var reply = await client.SendRawAsync("""[{"jsonrpc":"2.0","id":1,"method":"ping"}]""",
            configure: r => r.Headers.Add("MCP-Protocol-Version", "2025-06-18"));

        reply.Status.Should().Be(HttpStatusCode.BadRequest);
        reply.ErrorCode.Should().Be(-32600);
    }

    [Fact]
    public async Task Batches_are_processed_for_2025_03_26()
    {
        using var client = await _fixture.LoggedInAsync("alice");

        var reply = await client.SendRawAsync(
            """[{"jsonrpc":"2.0","id":1,"method":"ping"},{"jsonrpc":"2.0","method":"notifications/initialized"},{"jsonrpc":"2.0","id":2,"method":"initialize"},{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"get_customer","arguments":{"id":"9"}}}]""",
            configure: r => r.Headers.Add("MCP-Protocol-Version", "2025-03-26"));

        reply.Status.Should().Be(HttpStatusCode.OK, reply.Raw);
        var items = reply.Json!.Value.EnumerateArray().ToList();
        items.Should().HaveCount(3, "the notification gets no reply");
        items[0].GetProperty("id").GetInt32().Should().Be(1);
        items[1].GetProperty("error").GetProperty("code").GetInt32().Should().Be(-32600, "initialize is not allowed in a batch");
        items[2].GetProperty("result").GetProperty("isError").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Invalid_params_are_reported_as_invalid_params()
    {
        using var client = await _fixture.LoggedInAsync("alice");

        var reply = await client.SendRawAsync("""{"jsonrpc":"2.0","id":"p1","method":"tools/call","params":[1]}""");

        reply.Status.Should().Be(HttpStatusCode.OK);
        reply.ErrorCode.Should().Be(-32602);
        reply.Json!.Value.GetProperty("id").GetString().Should().Be("p1");
    }

    [Theory]
    [InlineData("application/json; charset=\"utf-8\"", HttpStatusCode.OK)]
    [InlineData("application/json; charset=UTF-8", HttpStatusCode.OK)]
    [InlineData("application/json; charset=latin1", HttpStatusCode.UnsupportedMediaType)]
    public async Task Charset_parameter_is_parsed(string contentType, HttpStatusCode expected)
    {
        using var client = await _fixture.LoggedInAsync("alice");

        var reply = await client.SendRawAsync("""{"jsonrpc":"2.0","id":1,"method":"ping"}""", contentType);

        reply.Status.Should().Be(expected);
    }

    [Fact]
    public async Task Completion_is_not_advertised_and_not_implemented()
    {
        using var client = await _fixture.LoggedInAsync("alice");

        var init = await client.RpcAsync("initialize", new { protocolVersion = "2025-06-18" });
        init.Result.GetProperty("capabilities").TryGetProperty("completions", out _).Should().BeFalse();
        (await client.RpcAsync("completion/complete", new { })).ErrorCode.Should().Be(-32601);
    }

    [Theory]
    [InlineData("""{"id":1,"method":"ping"}""")]
    [InlineData("""{"jsonrpc":"1.0","id":1,"method":"ping"}""")]
    [InlineData("""{"jsonrpc":"2.0","id":{"a":1},"method":"ping"}""")]
    [InlineData("""{"jsonrpc":"2.0","id":1}""")]
    public async Task Invalid_requests_are_rejected(string body)
    {
        using var client = await _fixture.LoggedInAsync("alice");

        var reply = await client.SendRawAsync(body);

        reply.Status.Should().Be(HttpStatusCode.BadRequest);
        reply.ErrorCode.Should().Be(-32600);
    }

    [Fact]
    public async Task Malformed_json_is_a_parse_error()
    {
        using var client = await _fixture.LoggedInAsync("alice");

        var reply = await client.SendRawAsync("{not json");

        reply.Status.Should().Be(HttpStatusCode.BadRequest);
        reply.ErrorCode.Should().Be(-32700);
    }

    [Fact]
    public async Task Id_is_echoed_verbatim()
    {
        using var client = await _fixture.LoggedInAsync("alice");

        var reply = await client.SendRawAsync("""{"jsonrpc":"2.0","id":"req-é-1","method":"ping"}""");

        reply.Json!.Value.GetProperty("id").GetString().Should().Be("req-é-1");
    }

    [Fact]
    public async Task Unknown_method_is_method_not_found()
    {
        using var client = await _fixture.LoggedInAsync("alice");

        var reply = await client.RpcAsync("resources/list");

        reply.ErrorCode.Should().Be(-32601);
    }

    [Fact]
    public async Task Get_is_method_not_allowed()
    {
        using var client = await _fixture.LoggedInAsync("alice");

        var reply = await client.SendRawAsync(string.Empty, method: HttpMethod.Get);

        reply.Status.Should().Be(HttpStatusCode.MethodNotAllowed);
    }

    [Fact]
    public async Task Oversized_bodies_are_rejected()
    {
        using var client = await _fixture.LoggedInAsync("alice");
        var padding = new string('a', 2 * 1024 * 1024);

        var reply = await client.SendRawAsync($$$"""{"jsonrpc":"2.0","id":1,"method":"ping","params":{"x":"{{{padding}}}"}}""");

        reply.Status.Should().Be(HttpStatusCode.RequestEntityTooLarge);
    }

    [Fact]
    public async Task Unsupported_protocol_version_header_is_rejected()
    {
        using var client = await _fixture.LoggedInAsync("alice");

        var reply = await client.RpcAsync("ping", configure: r => r.Headers.Add("MCP-Protocol-Version", "1999-01-01"));

        reply.Status.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Tools_list_has_valid_schemas_for_minimal_and_mvc_tools()
    {
        using var client = await _fixture.LoggedInAsync("alice");

        var reply = await client.RpcAsync("tools/list");

        reply.ToolNames.Should().Contain(new[] { "get_customer", "create_customer", "create_order", "get_order", "rag_search" });
        reply.ToolNames.Should().NotContain(new[] { "get_api_catalog", "get_api_orders" }, "read-only endpoints are not executable");

        foreach (var tool in reply.Result.GetProperty("tools").EnumerateArray())
        {
            tool.GetProperty("inputSchema").GetProperty("type").GetString().Should().Be("object", tool.ToString());
            tool.GetProperty("name").GetString().Should().MatchRegex("^[A-Za-z0-9_.-]{1,64}$");
        }

        var getCustomer = reply.Tool("get_customer");
        getCustomer.GetProperty("description").GetString().Should().Be("Gets a customer by id");
        getCustomer.GetProperty("inputSchema").GetProperty("required").EnumerateArray().Select(e => e.GetString()).Should().Contain("id");

        var createOrder = reply.Tool("create_order").GetProperty("inputSchema");
        createOrder.GetProperty("properties").TryGetProperty("productId", out _).Should().BeTrue();
        createOrder.GetProperty("properties").GetProperty("quantity").GetProperty("type").GetString().Should().Be("integer");
        createOrder.GetProperty("required").EnumerateArray().Select(e => e.GetString()).Should().Contain("productId");
    }

    [Fact]
    public async Task Rag_search_only_returns_visible_endpoints()
    {
        using var client = await _fixture.LoggedInAsync("bob", "sales");

        var reply = await client.CallToolAsync("rag_search", new { query = "finance total leads customers" });

        reply.IsToolError.Should().BeFalse(reply.ToolText);
        reply.ToolText.Should().NotContain("finance_summary").And.NotContain("/api/leads");
    }
}

public sealed class McpRateLimitTests
{
    [Fact]
    public async Task Rate_limit_is_per_client_not_global()
    {
        await using var host = await TestHost.StartAsync(b => CrmApp.ConfigureServices(b), app => CrmApp.Configure(app));
        using var alice = host.CreateClient();
        await alice.LoginAsync("alice");
        using var bob = host.CreateClient();
        await bob.LoginAsync("bob");

        (await alice.CallToolAsync("limited")).IsToolError.Should().BeFalse();
        (await alice.CallToolAsync("limited")).IsToolError.Should().BeFalse();
        var third = await alice.CallToolAsync("limited");
        third.IsToolError.Should().BeTrue();
        third.ToolText.Should().Contain("Rate limit exceeded");

        (await bob.CallToolAsync("limited")).IsToolError.Should().BeFalse("bob has his own budget");
    }
}

public sealed class McpAuthorizationOptionTests
{
    [Fact]
    public async Task Fallback_policy_protects_tools_even_when_the_mcp_endpoint_is_public()
    {
        await using var host = await TestHost.StartAsync(
            b => CrmApp.ConfigureServices(b,
                o => o.RequireAuthentication = false,
                a => a.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build()),
            app => CrmApp.Configure(app));
        using var anonymous = host.CreateClient();

        (await anonymous.RpcAsync("ping")).Status.Should().Be(HttpStatusCode.OK);
        var reply = await anonymous.CallToolAsync("open_tool");

        reply.IsToolError.Should().BeTrue();
        reply.ToolText.Should().Contain("401");
    }

    [Fact]
    public async Task Public_endpoint_without_policies_is_callable_when_mcp_is_public()
    {
        await using var host = await TestHost.StartAsync(b => CrmApp.ConfigureServices(b, o => o.RequireAuthentication = false), app => CrmApp.Configure(app));
        using var anonymous = host.CreateClient();

        var reply = await anonymous.CallToolAsync("open_tool");

        reply.IsToolError.Should().BeFalse(reply.ToolText);
        (await anonymous.CallToolAsync("get_customer", new { id = "1" })).ToolText.Should().Contain("401");
    }

    [Fact]
    public async Task Mcp_authorization_policy_is_applied()
    {
        await using var host = await TestHost.StartAsync(
            b => CrmApp.ConfigureServices(b,
                o => o.AuthorizationPolicy = "ai-users",
                a => a.AddPolicy("ai-users", p => p.RequireRole("ai"))),
            app => CrmApp.Configure(app));

        using var plain = host.CreateClient();
        await plain.LoginAsync("bob", "sales");
        (await plain.RpcAsync("ping")).Status.Should().Be(HttpStatusCode.Forbidden);

        using var ai = host.CreateClient();
        await ai.LoginAsync("eve", "ai");
        (await ai.RpcAsync("ping")).Status.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task MapMcp_supports_endpoint_conventions()
    {
        await using var host = await TestHost.StartAsync(
            b => CrmApp.ConfigureServices(b),
            app => CrmApp.Configure(app, mapMcp: true));
        using var anonymous = host.CreateClient();
        (await anonymous.RpcAsync("ping")).Status.Should().Be(HttpStatusCode.Unauthorized);

        using var alice = host.CreateClient();
        await alice.LoginAsync("alice");
        var reply = await alice.CallToolAsync("get_customer", new { id = "5" });
        reply.IsToolError.Should().BeFalse(reply.ToolText);
        reply.ToolText.Should().Contain("\"requestedBy\":\"alice\"");
    }

    [Fact]
    public async Task Duplicate_tool_names_resolve_deterministically()
    {
        await using var host = await TestHost.StartAsync(
            b =>
            {
                b.Services.UseMcp(o => o.RequireAuthentication = false);
            },
            app =>
            {
                app.UseMcp();
                app.MapGet("/b", () => "b").AiTool("same");
                app.MapGet("/a", () => "a").AiTool("same");
            });
        using var client = host.CreateClient();

        var list = await client.RpcAsync("tools/list");
        list.ToolNames.Count(n => n == "same").Should().Be(1);
        (await client.CallToolAsync("same")).ToolText.Should().Be("a");
    }

    [Fact]
    public async Task Search_prompt_is_only_offered_with_rag()
    {
        await using var host = await TestHost.StartAsync(b => CrmApp.ConfigureServices(b, withRag: false), app => CrmApp.Configure(app));
        using var client = host.CreateClient();
        await client.LoginAsync("alice");

        var prompts = await client.RpcAsync("prompts/list");
        prompts.Result.GetProperty("prompts").EnumerateArray().Select(p => p.GetProperty("name").GetString())
            .Should().Equal("list_available_tools");
        (await client.RpcAsync("prompts/get", new { name = "search_api" })).ErrorCode.Should().Be(-32602);
        (await client.RpcAsync("tools/list")).ToolNames.Should().NotContain("rag_search");
    }

    [Fact]
    public async Task AuthorizeToolCall_hook_runs_with_the_tool_context()
    {
        await using var host = await TestHost.StartAsync(
            b => CrmApp.ConfigureServices(b, o => o.AuthorizeToolCall = (ctx, tool) =>
                new ValueTask<bool>(ctx.Request.Headers["X-Tenant"] == "acme" && ctx.Request.Path.StartsWithSegments("/api"))),
            app => CrmApp.Configure(app));
        using var client = host.CreateClient();
        await client.LoginAsync("alice");

        var denied = await client.CallToolAsync("get_customer", new { id = "1" });
        denied.IsToolError.Should().BeTrue();
        denied.ToolText.Should().Contain("403");

        var allowed = await client.CallToolAsync("get_customer", new { id = "1" }, r => r.Headers.Add("X-Tenant", "acme"));
        allowed.IsToolError.Should().BeFalse(allowed.ToolText);
    }

    [Fact]
    public async Task Anonymous_endpoint_rejects_dns_rebinding_but_accepts_trusted_same_origin()
    {
        await using var host = await TestHost.StartAsync(b => CrmApp.ConfigureServices(b, o => o.RequireAuthentication = false), app => CrmApp.Configure(app));
        using var client = host.CreateClient();
        var port = host.BaseAddress.Port;

        var rebinding = await client.RpcAsync("ping", configure: r =>
        {
            r.Headers.Host = $"evil.example:{port}";
            r.Headers.Add("Origin", $"http://evil.example:{port}");
        });
        rebinding.Status.Should().Be(HttpStatusCode.Forbidden);

        var loopback = await client.RpcAsync("ping", configure: r => r.Headers.Add("Origin", $"http://127.0.0.1:{port}"));
        loopback.Status.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Anonymous_endpoint_trusts_configured_hosts()
    {
        await using var host = await TestHost.StartAsync(
            b => CrmApp.ConfigureServices(b, o =>
            {
                o.RequireAuthentication = false;
                o.AllowedHosts.Add("*.example.com");
            }),
            app => CrmApp.Configure(app));
        using var client = host.CreateClient();
        var port = host.BaseAddress.Port;

        var reply = await client.RpcAsync("ping", configure: r =>
        {
            r.Headers.Host = $"app.example.com:{port}";
            r.Headers.Add("Origin", $"http://app.example.com:{port}");
        });

        reply.Status.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Missing_service_registration_fails_fast()
    {
        var act = async () =>
        {
            await using var host = await TestHost.StartAsync(_ => { }, app => app.UseMcp());
        };

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*UseMcp()*");
    }
}

#if NET10_0_OR_GREATER
/// <summary>
/// The production failure: a .NET 10 API using Microsoft.AspNetCore.OpenApi (OpenAPI.NET 2.x).
/// 3.0.0 answered 500 to every /mcp call here.
/// </summary>
public sealed class Net10OpenApiCoexistenceTests
{
    [Fact]
    public async Task Works_alongside_Microsoft_AspNetCore_OpenApi()
    {
        await using var host = await TestHost.StartAsync(
            b =>
            {
                b.Services.AddOpenApi();
                CrmApp.ConfigureServices(b);
            },
            app =>
            {
                app.MapOpenApi();
                CrmApp.Configure(app);
            });
        using var client = host.CreateClient();
        await client.LoginAsync("alice");

        (await client.RpcAsync("initialize", new { protocolVersion = "2025-06-18" })).Status.Should().Be(HttpStatusCode.OK);
        var list = await client.RpcAsync("tools/list");
        list.ToolNames.Should().Contain("get_customer");
        var call = await client.CallToolAsync("get_customer", new { id = "1" });
        call.IsToolError.Should().BeFalse(call.ToolText);

        var openApi = await client.Http.GetStringAsync("/openapi/v1.json");
        openApi.Should().Contain("/api/customers/{id}");
    }
}
#endif
