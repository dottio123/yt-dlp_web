namespace yt_dlp_web.Tests;

using Microsoft.Extensions.Options;
using yt_dlp_web.Services;

public class DownloadLimiterTests
{
    [Fact]
    public async Task DownloadLimiter_BlocksBeyondMax()
    {
        var options = Options.Create(new YtDlpOptions { MaxConcurrentDownloads = 1 });
        var limiter = new DownloadLimiter(options);

        Assert.False(limiter.IsSaturated);

        // First WaitAsync completes immediately
        var t1 = limiter.WaitAsync(CancellationToken.None);
        await t1;
        Assert.True(t1.IsCompletedSuccessfully);
        Assert.True(limiter.IsSaturated);

        // Second WaitAsync is blocked
        var t2 = limiter.WaitAsync(CancellationToken.None);
        Assert.False(t2.IsCompleted);

        // After Release(), second completes
        limiter.Release();
        await t2;
        Assert.True(t2.IsCompletedSuccessfully);
        Assert.True(limiter.IsSaturated);

        limiter.Release();
        Assert.False(limiter.IsSaturated);
    }

    [Fact]
    public async Task DownloadLimiter_WaitIsCancellable()
    {
        var options = Options.Create(new YtDlpOptions { MaxConcurrentDownloads = 1 });
        var limiter = new DownloadLimiter(options);

        // Hold slot
        await limiter.WaitAsync(CancellationToken.None);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await limiter.WaitAsync(cts.Token);
        });

        limiter.Release();
    }

    [Fact]
    public async Task DownloadLimiter_ZeroMeansUnlimited()
    {
        var options = Options.Create(new YtDlpOptions { MaxConcurrentDownloads = 0 });
        var limiter = new DownloadLimiter(options);

        var tasks = new List<Task>();
        for (int i = 0; i < 50; i++)
        {
            tasks.Add(limiter.WaitAsync(CancellationToken.None));
        }

        await Task.WhenAll(tasks);
        Assert.All(tasks, t => Assert.True(t.IsCompletedSuccessfully));
        Assert.False(limiter.IsSaturated);

        // Release never throws
        limiter.Release();
        Assert.False(limiter.IsSaturated);
    }
}
