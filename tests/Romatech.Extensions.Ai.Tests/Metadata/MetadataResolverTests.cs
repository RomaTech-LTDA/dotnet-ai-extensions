using FluentAssertions;
using Romatech.Extensions.Ai.Metadata.Attributes;
using Romatech.Extensions.Ai.Metadata.Resolution;
using Romatech.Extensions.Ai.Shared.Models;
using Xunit;

namespace Romatech.Extensions.Ai.Tests.Metadata;

public class MetadataResolverTests
{
    [Fact]
    public void ResolveExposureLevel_WithAiHidden_ReturnsHidden()
    {
        var method = typeof(TestController).GetMethod(nameof(TestController.HiddenMethod))!;
        var result = MetadataResolver.ResolveExposureLevel(method);
        result.Should().Be(AiExposureLevel.Hidden);
    }

    [Fact]
    public void ResolveExposureLevel_WithAiTool_ReturnsExecutable()
    {
        var method = typeof(TestController).GetMethod(nameof(TestController.ToolMethod))!;
        var result = MetadataResolver.ResolveExposureLevel(method);
        result.Should().Be(AiExposureLevel.Executable);
    }

    [Fact]
    public void ResolveExposureLevel_WithNoAttribute_ReturnsReadOnly()
    {
        var method = typeof(TestController).GetMethod(nameof(TestController.PlainMethod))!;
        var result = MetadataResolver.ResolveExposureLevel(method);
        result.Should().Be(AiExposureLevel.ReadOnly);
    }

    [Fact]
    public void ResolveToolName_WithCustomName_ReturnsCustomName()
    {
        var method = typeof(TestController).GetMethod(nameof(TestController.NamedToolMethod))!;
        var result = MetadataResolver.ResolveToolName(method);
        result.Should().Be("custom_create_order");
    }

    [Fact]
    public void ResolveToolName_WithOperationId_ReturnsOperationId()
    {
        var method = typeof(TestController).GetMethod(nameof(TestController.ToolMethod))!;
        var result = MetadataResolver.ResolveToolName(method, "getOrders");
        result.Should().Be("getOrders");
    }

    [Fact]
    public void ResolveToolName_WithNoOverride_ReturnsSnakeCase()
    {
        var method = typeof(TestController).GetMethod(nameof(TestController.ToolMethod))!;
        var result = MetadataResolver.ResolveToolName(method);
        result.Should().Be("tool_method");
    }

    [Fact]
    public void ResolveDescription_ReturnsAttributeValue()
    {
        var method = typeof(TestController).GetMethod(nameof(TestController.DescribedMethod))!;
        var result = MetadataResolver.ResolveDescription(method);
        result.Should().Be("Creates a new order");
    }

    [Fact]
    public void ResolveCategory_ReturnsAttributeValue()
    {
        var method = typeof(TestController).GetMethod(nameof(TestController.CategorizedMethod))!;
        var result = MetadataResolver.ResolveCategory(method);
        result.Should().Be("Payments");
    }

    [Fact]
    public void ResolveRole_ReturnsAttributeValue()
    {
        var method = typeof(TestController).GetMethod(nameof(TestController.RoleProtectedMethod))!;
        var result = MetadataResolver.ResolveRole(method);
        result.Should().Be("admin");
    }

    [Fact]
    public void ResolveRateLimit_ReturnsAttributeValue()
    {
        var method = typeof(TestController).GetMethod(nameof(TestController.RateLimitedMethod))!;
        var result = MetadataResolver.ResolveRateLimit(method);
        result.Should().Be(10);
    }

    [Fact]
    public void ResolveContextPriority_ReturnsAttributeValue()
    {
        var method = typeof(TestController).GetMethod(nameof(TestController.PrioritizedMethod))!;
        var result = MetadataResolver.ResolveContextPriority(method);
        result.Should().Be(100);
    }

    // Test fixtures
    private class TestController
    {
        [AiHidden]
        public void HiddenMethod() { }

        [AiTool]
        public void ToolMethod() { }

        public void PlainMethod() { }

        [AiTool("custom_create_order")]
        public void NamedToolMethod() { }

        [AiDescription("Creates a new order")]
        public void DescribedMethod() { }

        [AiCategory("Payments")]
        public void CategorizedMethod() { }

        [AiRole("admin")]
        public void RoleProtectedMethod() { }

        [AiRateLimit(10)]
        public void RateLimitedMethod() { }

        [AiContextPriority(100)]
        public void PrioritizedMethod() { }
    }
}
