namespace yt_dlp_web.Services;

using Microsoft.Extensions.Options;

public sealed class DownloadLimiter
{
    private readonly SemaphoreSlim? _semaphore;

    public DownloadLimiter(IOptions<YtDlpOptions> options)
    {
        var max = options.Value.MaxConcurrentDownloads;
        if (max > 0)
        {
            _semaphore = new SemaphoreSlim(max, max);
        }
    }

    public bool IsSaturated => _semaphore != null && _semaphore.CurrentCount == 0;

    public async Task WaitAsync(CancellationToken ct)
    {
        if (_semaphore != null)
        {
            await _semaphore.WaitAsync(ct);
        }
    }

    public void Release()
    {
        _semaphore?.Release();
    }
}
