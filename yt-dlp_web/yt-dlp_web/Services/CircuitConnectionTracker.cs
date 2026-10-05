using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.Extensions.Options;

namespace yt_dlp_web.Services;

public sealed class CircuitConnectionTracker : CircuitHandler, IDisposable
{
    private readonly object _gate = new();
    private readonly YtDlpOptions _options;
    private CancellationTokenSource _cts = new();
    private bool _closed;
    private bool _disposed;

    public CircuitConnectionTracker(IOptions<YtDlpOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
    }

    public CancellationToken DisconnectedToken
    {
        get
        {
            lock (_gate)
            {
                if (_disposed || _closed)
                {
                    return new CancellationToken(canceled: true);
                }

                try
                {
                    return _cts.Token;
                }
                catch (ObjectDisposedException)
                {
                    return new CancellationToken(canceled: true);
                }
            }
        }
    }

    public override Task OnConnectionDownAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_disposed || _closed)
            {
                return Task.CompletedTask;
            }

            try
            {
                _cts.CancelAfter(TimeSpan.FromSeconds(Math.Max(0, _options.DisconnectGraceSeconds)));
            }
            catch (ObjectDisposedException)
            {
            }
        }

        return Task.CompletedTask;
    }

    public override Task OnConnectionUpAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_disposed || _closed)
            {
                return Task.CompletedTask;
            }

            if (_cts.IsCancellationRequested)
            {
                var oldCts = _cts;
                _cts = new CancellationTokenSource();
                try
                {
                    oldCts.Dispose();
                }
                catch (ObjectDisposedException)
                {
                }
            }
            else
            {
                try
                {
                    _cts.CancelAfter(Timeout.InfiniteTimeSpan);
                }
                catch (ObjectDisposedException)
                {
                }
            }
        }

        return Task.CompletedTask;
    }

    public override Task OnCircuitClosedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return Task.CompletedTask;
            }

            _closed = true;
            try
            {
                _cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            try
            {
                _cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            _cts.Dispose();
        }
    }
}
