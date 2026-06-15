using System.Collections.Concurrent;

namespace Romatech.Extensions.Ai.Mcp.Security;

/// <summary>
/// A thread-safe sliding window rate limiter for MCP tool calls.
/// </summary>
public sealed class SlidingWindowRateLimiter : IRateLimiter
{
    private readonly ConcurrentDictionary<string, ToolRateState> _states = new();

    /// <inheritdoc />
    public bool TryAcquire(string toolName, int maxRequestsPerMinute)
    {
        var state = _states.GetOrAdd(toolName, _ => new ToolRateState());
        return state.TryAcquire(maxRequestsPerMinute);
    }

    private sealed class ToolRateState
    {
        private readonly object _lock = new();
        private readonly Queue<DateTime> _timestamps = new();

        public bool TryAcquire(int maxPerMinute)
        {
            lock (_lock)
            {
                var now = DateTime.UtcNow;
                var windowStart = now.AddMinutes(-1);

                // Remove expired entries
                while (_timestamps.Count > 0 && _timestamps.Peek() < windowStart)
                {
                    _timestamps.Dequeue();
                }

                if (_timestamps.Count >= maxPerMinute)
                    return false;

                _timestamps.Enqueue(now);
                return true;
            }
        }
    }
}
