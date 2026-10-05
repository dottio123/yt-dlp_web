using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.Extensions.Options;
using yt_dlp_web.Services;

namespace yt_dlp_web.Tests;

public class CircuitConnectionTrackerTests
{
    [Fact]
    public async Task OnConnectionDownAsync_Grace0_CancelsWithin1Second()
    {
        var options = Options.Create(new YtDlpOptions { DisconnectGraceSeconds = 0 });
        using var tracker = new CircuitConnectionTracker(options);

        await tracker.OnConnectionDownAsync(null!, CancellationToken.None);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        while (!tracker.DisconnectedToken.IsCancellationRequested && stopwatch.ElapsedMilliseconds < 1000)
        {
            await Task.Delay(25);
        }

        Assert.True(tracker.DisconnectedToken.IsCancellationRequested);
    }

    [Fact]
    public async Task OnConnectionDownThenUpAsync_Grace1_DoesNotCancel()
    {
        var options = Options.Create(new YtDlpOptions { DisconnectGraceSeconds = 1 });
        using var tracker = new CircuitConnectionTracker(options);

        await tracker.OnConnectionDownAsync(null!, CancellationToken.None);
        await tracker.OnConnectionUpAsync(null!, CancellationToken.None);

        await Task.Delay(2000);

        Assert.False(tracker.DisconnectedToken.IsCancellationRequested);
    }

    [Fact]
    public async Task OnCircuitClosedAsync_CancelsImmediately()
    {
        var options = Options.Create(new YtDlpOptions { DisconnectGraceSeconds = 30 });
        using var tracker = new CircuitConnectionTracker(options);

        await tracker.OnCircuitClosedAsync(null!, CancellationToken.None);

        Assert.True(tracker.DisconnectedToken.IsCancellationRequested);
    }

    [Fact]
    public void Dispose_GuardsAgainstObjectDisposedException()
    {
        var options = Options.Create(new YtDlpOptions { DisconnectGraceSeconds = 30 });
        var tracker = new CircuitConnectionTracker(options);

        tracker.Dispose();

        var downTask = tracker.OnConnectionDownAsync(null!, CancellationToken.None);
        Assert.True(downTask.IsCompleted);

        var upTask = tracker.OnConnectionUpAsync(null!, CancellationToken.None);
        Assert.True(upTask.IsCompleted);

        var closedTask = tracker.OnCircuitClosedAsync(null!, CancellationToken.None);
        Assert.True(closedTask.IsCompleted);

        tracker.Dispose();
    }

    [Fact]
    public async Task ReconnectAfterGrace_ProvidesFreshToken()
    {
        var options = Options.Create(new YtDlpOptions { DisconnectGraceSeconds = 0 });
        using var tracker = new CircuitConnectionTracker(options);

        var oldToken = tracker.DisconnectedToken;
        await tracker.OnConnectionDownAsync(null!, CancellationToken.None);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        while (!oldToken.IsCancellationRequested && stopwatch.ElapsedMilliseconds < 1000)
        {
            await Task.Delay(25);
        }

        Assert.True(oldToken.IsCancellationRequested);

        await tracker.OnConnectionUpAsync(null!, CancellationToken.None);

        Assert.True(oldToken.IsCancellationRequested);
        Assert.False(tracker.DisconnectedToken.IsCancellationRequested);
    }

    [Fact]
    public async Task ReconnectAfterCircuitClosed_StaysCancelled()
    {
        var options = Options.Create(new YtDlpOptions { DisconnectGraceSeconds = 30 });
        using var tracker = new CircuitConnectionTracker(options);

        await tracker.OnCircuitClosedAsync(null!, CancellationToken.None);
        await tracker.OnConnectionUpAsync(null!, CancellationToken.None);

        Assert.True(tracker.DisconnectedToken.IsCancellationRequested);
    }
}
