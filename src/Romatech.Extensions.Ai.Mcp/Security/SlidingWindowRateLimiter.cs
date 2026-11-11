using System.Collections.Concurrent;

namespace Romatech.Extensions.Ai.Mcp.Security;

/// <summary>
/// A thread-safe sliding window rate limiter. Keys are opaque (the MCP layer uses "client + tool"),
/// and idle keys are evicted so memory stays bounded no matter how many distinct clients call.
/// </summary>
public sealed class SlidingWindowRateLimiter : IRateLimiter
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
    private const int SweepEvery = 1024;

    private readonly ConcurrentDictionary<string, KeyState> _states = new(StringComparer.Ordinal);
    private readonly Func<DateTime> _utcNow;
    private int _calls;

    /// <summary>Creates a limiter that uses the system clock.</summary>
    public SlidingWindowRateLimiter() : this(() => DateTime.UtcNow)
    {
    }

    internal SlidingWindowRateLimiter(Func<DateTime> utcNow)
    {
        _utcNow = utcNow;
    }

    /// <summary>Number of keys currently tracked (for diagnostics and tests).</summary>
    internal int TrackedKeys => _states.Count;

    /// <inheritdoc />
    public bool TryAcquire(string toolName, int maxRequestsPerMinute)
    {
        if (toolName is null) throw new ArgumentNullException(nameof(toolName));
        if (maxRequestsPerMinute <= 0) return false;

        var now = _utcNow();
        if (Interlocked.Increment(ref _calls) % SweepEvery == 0)
            Sweep(now);

        while (true)
        {
            var state = _states.GetOrAdd(toolName, _ => new KeyState());
            lock (state)
            {
                if (state.Evicted)
                    continue; // lost a race with Sweep; take the fresh state

                state.Trim(now - Window);
                if (state.Timestamps.Count >= maxRequestsPerMinute)
                    return false;

                state.Timestamps.Enqueue(now);
                return true;
            }
        }
    }

    private void Sweep(DateTime now)
    {
        var windowStart = now - Window;
        foreach (var pair in _states)
        {
            var state = pair.Value;
            lock (state)
            {
                state.Trim(windowStart);
                if (state.Timestamps.Count != 0)
                    continue;
                state.Evicted = true;
            }
            _states.TryRemove(pair);
        }
    }

    private sealed class KeyState
    {
        public Queue<DateTime> Timestamps { get; } = new();

        public bool Evicted { get; set; }

        public void Trim(DateTime windowStart)
        {
            while (Timestamps.Count > 0 && Timestamps.Peek() <= windowStart)
                Timestamps.Dequeue();
        }
    }
}
