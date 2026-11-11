using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using FluentAssertions;
using Romatech.Extensions.Ai.Mcp.Security;
using Romatech.Extensions.Ai.Mcp.Services;
using Romatech.Extensions.Ai.Metadata.Attributes;
using Romatech.Extensions.Ai.Metadata.Resolution;
using Romatech.Extensions.Ai.Shared.Models;
using Romatech.Extensions.Ai.Swagger.Discovery;
using Romatech.Extensions.Ai.Swagger.Schema;
using Xunit;

namespace Romatech.Extensions.Ai.Tests.Discovery;

public class JsonSchemaGeneratorTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    public enum Status { Active, Blocked }

    public sealed record Address(string Street, [property: JsonPropertyName("zip_code")] string Zip);

    public sealed record Customer(
        [Required] string Name,
        int Age,
        decimal? Credit,
        Guid Id,
        DateTime CreatedAt,
        Status Status,
        List<string> Tags,
        Dictionary<string, int> Scores,
        Address Address,
        [property: JsonIgnore] string Secret);

    public sealed class Node
    {
        public string Value { get; set; } = "";
        public Node? Next { get; set; }
    }

    [Fact]
    public void Generates_object_schema_honouring_naming_and_attributes()
    {
        var schema = JsonSchemaGenerator.Generate(typeof(Customer), Web);
        var props = schema["properties"]!.AsObject();

        props.Select(p => p.Key).Should().BeEquivalentTo(
            "name", "age", "credit", "id", "createdAt", "status", "tags", "scores", "address");
        props["age"]!["type"]!.GetValue<string>().Should().Be("integer");
        props["credit"]!["type"]!.GetValue<string>().Should().Be("number");
        props["id"]!["format"]!.GetValue<string>().Should().Be("uuid");
        props["createdAt"]!["format"]!.GetValue<string>().Should().Be("date-time");
        props["status"]!["type"]!.GetValue<string>().Should().Be("integer", "no string enum converter is configured");
        props["tags"]!["items"]!["type"]!.GetValue<string>().Should().Be("string");
        props["scores"]!["additionalProperties"]!["type"]!.GetValue<string>().Should().Be("integer");
        props["address"]!["properties"]!.AsObject().Select(p => p.Key).Should().BeEquivalentTo("street", "zip_code");
        schema["required"]!.AsArray().Select(n => n!.GetValue<string>()).Should().Equal("name");
    }

    [Fact]
    public void String_enums_follow_the_configured_converter()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());

        var schema = JsonSchemaGenerator.Generate(typeof(Status), options);

        schema["type"]!.GetValue<string>().Should().Be("string");
        schema["enum"]!.AsArray().Select(n => n!.GetValue<string>()).Should().Equal("Active", "Blocked");
    }

    [Fact]
    public void Recursive_types_terminate()
    {
        var schema = JsonSchemaGenerator.Generate(typeof(Node), Web);

        schema["properties"]!["next"]!["type"]!.GetValue<string>().Should().Be("object");
        schema.ToJsonString().Length.Should().BeLessThan(500);
    }

    [Fact]
    public void Free_form_types_produce_empty_schema()
    {
        JsonSchemaGenerator.Generate(typeof(JsonElement), Web).Count.Should().Be(0);
        JsonSchemaGenerator.Generate(typeof(object), Web).Count.Should().Be(0);
        JsonSchemaGenerator.Generate(typeof(JsonObject), Web).Count.Should().Be(0);
    }
}

public class ToolNameTests
{
    [Theory]
    [InlineData("get_customer", "get_customer")]
    [InlineData("get_/api/customers/id", "get_api_customers_id")]
    [InlineData("criar pedido!", "criar_pedido")]
    [InlineData("///", "tool")]
    public void Sanitizes_names(string input, string expected)
        => ApiExplorerEndpointDiscoveryProvider.SanitizeToolName(input).Should().Be(expected);

    [Fact]
    public void Long_names_are_capped_and_stay_unique()
    {
        var a = ApiExplorerEndpointDiscoveryProvider.SanitizeToolName(new string('a', 80) + "1");
        var b = ApiExplorerEndpointDiscoveryProvider.SanitizeToolName(new string('a', 80) + "2");

        a.Length.Should().Be(64);
        b.Length.Should().Be(64);
        a.Should().NotBe(b);
    }
}

public class RouteValueSafetyTests
{
    [Theory]
    [InlineData("42", false, true)]
    [InlineData("a b%2F", false, true)]
    [InlineData("../leads", false, false)]
    [InlineData("..", false, false)]
    [InlineData(".", false, false)]
    [InlineData("a/b", false, false)]
    [InlineData("a\\b", false, false)]
    [InlineData("line\nbreak", false, false)]
    [InlineData("docs/readme.md", true, true)]
    [InlineData("docs/../secret", true, false)]
    [InlineData("./x", true, false)]
    public void Only_values_an_http_request_could_produce_are_accepted(string value, bool catchAll, bool expected)
        => McpToolExecutor.IsSafeRouteValue(value, catchAll).Should().Be(expected);
}

public class EndpointMetadataResolverTests
{
    [Fact]
    public void Most_specific_metadata_wins_and_hidden_always_wins()
    {
        var metadata = new object[]
        {
            new AiCategoryAttribute("Controller"), new AiToolAttribute("controller_tool"), new AiRoleAttribute("a"),
            new AiCategoryAttribute("Action"), new AiToolAttribute("action_tool"), new AiRoleAttribute("b,c"),
        };

        EndpointMetadataResolver.ResolveCategory(metadata).Should().Be("Action");
        EndpointMetadataResolver.ResolveToolName(metadata).Should().Be("action_tool");
        EndpointMetadataResolver.ResolveRoles(metadata).Should().Equal("a", "b,c");
        EndpointMetadataResolver.ResolveExposureLevel(metadata).Should().Be(AiExposureLevel.Executable);
        EndpointMetadataResolver.ResolveExposureLevel(metadata.Append(new AiHiddenAttribute()).ToArray())
            .Should().Be(AiExposureLevel.Hidden);
    }

    [Fact]
    public void Role_requirements_are_and_of_ors()
    {
        var user = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            new[] { new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, "c"), new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, "a") },
            "test"));

        McpAuthorization.SatisfiesRoles(user, new[] { "a", "b,c" }).Should().BeTrue();
        McpAuthorization.SatisfiesRoles(user, new[] { "a", "b" }).Should().BeFalse();
        McpAuthorization.SatisfiesRoles(new System.Security.Claims.ClaimsPrincipal(), new[] { "a" }).Should().BeFalse();
        McpAuthorization.SatisfiesRoles(null, Array.Empty<string>()).Should().BeTrue();
    }
}

public class RateLimiterEvictionTests
{
    [Fact]
    public void Idle_keys_are_evicted_so_memory_stays_bounded()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var limiter = new SlidingWindowRateLimiter(() => now);

        for (var i = 0; i < 5000; i++)
            limiter.TryAcquire("client-" + i, 10).Should().BeTrue();

        now = now.AddMinutes(2);
        for (var i = 0; i < 2048; i++)
            limiter.TryAcquire("steady", 100000);

        limiter.TrackedKeys.Should().BeLessThan(10);
    }

    [Fact]
    public void Window_slides()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var limiter = new SlidingWindowRateLimiter(() => now);

        limiter.TryAcquire("k", 1).Should().BeTrue();
        limiter.TryAcquire("k", 1).Should().BeFalse();
        now = now.AddSeconds(61);
        limiter.TryAcquire("k", 1).Should().BeTrue();
    }

    [Fact]
    public void Non_positive_limit_denies()
        => new SlidingWindowRateLimiter().TryAcquire("k", 0).Should().BeFalse();
}
