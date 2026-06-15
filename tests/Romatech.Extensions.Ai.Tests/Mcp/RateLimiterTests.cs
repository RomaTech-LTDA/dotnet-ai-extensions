using FluentAssertions;
using Romatech.Extensions.Ai.Mcp.Security;
using Xunit;

namespace Romatech.Extensions.Ai.Tests.Mcp;

public class RateLimiterTests
{
    [Fact]
    public void TryAcquire_WithinLimit_ReturnsTrue()
    {
        var limiter = new SlidingWindowRateLimiter();

        var result = limiter.TryAcquire("test_tool", 10);

        result.Should().BeTrue();
    }

    [Fact]
    public void TryAcquire_ExceedsLimit_ReturnsFalse()
    {
        var limiter = new SlidingWindowRateLimiter();

        for (int i = 0; i < 5; i++)
        {
            limiter.TryAcquire("test_tool", 5);
        }

        var result = limiter.TryAcquire("test_tool", 5);
        result.Should().BeFalse();
    }

    [Fact]
    public void TryAcquire_DifferentTools_IndependentLimits()
    {
        var limiter = new SlidingWindowRateLimiter();

        for (int i = 0; i < 5; i++)
        {
            limiter.TryAcquire("tool_a", 5);
        }

        var result = limiter.TryAcquire("tool_b", 5);
        result.Should().BeTrue();
    }
}
