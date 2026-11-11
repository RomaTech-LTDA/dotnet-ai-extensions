using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;

namespace Romatech.Extensions.Ai.Mcp.Internal;

/// <summary>
/// Response feature for an in-process tool call that honours <c>OnStarting</c> / <c>OnCompleted</c>
/// (and therefore <c>Response.RegisterForDispose</c>), like a real server does.
/// </summary>
internal sealed class ToolResponseFeature : HttpResponseFeature
{
    private readonly List<(Func<object, Task> Callback, object State)> _onStarting = new();
    private readonly List<(Func<object, Task> Callback, object State)> _onCompleted = new();
    private readonly object _gate = new();
    private bool _started;

    public override bool HasStarted
    {
        get { lock (_gate) return _started; }
    }

    public override void OnStarting(Func<object, Task> callback, object state)
    {
        lock (_gate)
        {
            if (_started)
                throw new InvalidOperationException("The response has already started.");
            _onStarting.Add((callback, state));
        }
    }

    public override void OnCompleted(Func<object, Task> callback, object state)
    {
        lock (_gate) _onCompleted.Add((callback, state));
    }

    public async Task FireOnStartingAsync()
    {
        List<(Func<object, Task> Callback, object State)> callbacks;
        lock (_gate)
        {
            if (_started) return;
            _started = true;
            callbacks = _onStarting.ToList();
        }

        for (var i = callbacks.Count - 1; i >= 0; i--)
            await callbacks[i].Callback(callbacks[i].State).ConfigureAwait(false);
    }

    public async Task FireOnCompletedAsync(ILogger logger)
    {
        List<(Func<object, Task> Callback, object State)> callbacks;
        lock (_gate) callbacks = _onCompleted.ToList();

        for (var i = callbacks.Count - 1; i >= 0; i--)
        {
            try
            {
                await callbacks[i].Callback(callbacks[i].State).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "An OnCompleted callback of an MCP tool call failed");
            }
        }
    }
}

/// <summary>
/// Body feature that triggers <see cref="ToolResponseFeature.FireOnStartingAsync"/> before the first byte,
/// on <c>StartAsync</c> and on completion, as Kestrel does.
/// </summary>
internal sealed class ToolResponseBodyFeature : StreamResponseBodyFeature
{
    private readonly ToolResponseFeature _response;

    public ToolResponseBodyFeature(Stream body, ToolResponseFeature response)
        : base(new StartingStream(body, response))
    {
        _response = response;
    }

    public override async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _response.FireOnStartingAsync().ConfigureAwait(false);
        await base.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    public override async Task CompleteAsync()
    {
        await _response.FireOnStartingAsync().ConfigureAwait(false);
        await base.CompleteAsync().ConfigureAwait(false);
    }

    private sealed class StartingStream : Stream
    {
        private readonly Stream _inner;
        private readonly ToolResponseFeature _response;

        public StartingStream(Stream inner, ToolResponseFeature response)
        {
            _inner = inner;
            _response = response;
        }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _inner.Length;
        public override long Position { get => _inner.Position; set => throw new NotSupportedException(); }

        public override void Write(byte[] buffer, int offset, int count)
        {
            _response.FireOnStartingAsync().GetAwaiter().GetResult();
            _inner.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            _response.FireOnStartingAsync().GetAwaiter().GetResult();
            _inner.Write(buffer);
        }

        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            await _response.FireOnStartingAsync().ConfigureAwait(false);
            await _inner.WriteAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await _response.FireOnStartingAsync().ConfigureAwait(false);
            await _inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
        }

        public override void Flush()
        {
            _response.FireOnStartingAsync().GetAwaiter().GetResult();
            _inner.Flush();
        }

        public override async Task FlushAsync(CancellationToken cancellationToken)
        {
            await _response.FireOnStartingAsync().ConfigureAwait(false);
            await _inner.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
