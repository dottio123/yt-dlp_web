using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Options;

namespace yt_dlp_web.Services;

public class DownloadProgressUpdate
{
    public double Percent { get; set; }
    public string? Speed { get; set; }
    public string? Eta { get; set; }
    public string? DownloadedSize { get; set; }
    public string? TotalSize { get; set; }
    public string StatusMessage { get; set; } = "Initializing...";
    public bool IsIndeterminate { get; set; } = true;
}

public class DownloadRequest
{
    public string? Url { get; set; }
    public string? Format { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public string? ClientIp { get; set; }
    public bool ExtractAudio { get; set; }
    public string? AudioFormat { get; set; }
    public bool DownloadSubs { get; set; }
    public string? SubLangs { get; set; }
    public bool IncludeThumbnail { get; set; }
}

public class DownloadFileResult
{
    public string File { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
}

public class DownloadResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? Url { get; set; }
    public string? File { get; set; }
    public string? ThumbnailUrl { get; set; }
    public string? ThumbnailFile { get; set; }
    public List<DownloadFileResult> Subtitles { get; set; } = new();
}

public interface IDownloadService
{
    Task<DownloadResult> DownloadAsync(DownloadRequest request, Action<DownloadProgressUpdate>? onProgress = null, CancellationToken cancellationToken = default);
}

public class DownloadService : IDownloadService
{
    private readonly ILoggingService _logger;
    private readonly string _downloadsPath;
    private readonly string? _denoPath;
    private readonly YtDlpOptions _options;
    private readonly DownloadLimiter _limiter;

    public DownloadService(ILoggingService logger, IDownloadStore store, IOptions<YtDlpOptions> options, DownloadLimiter limiter)
    {
        _logger = logger;
        _downloadsPath = store.RootPath;
        _options = options.Value;
        _denoPath = _options.DenoPath;
        _limiter = limiter;
    }

    public async Task<DownloadResult> DownloadAsync(
        DownloadRequest req,
        Action<DownloadProgressUpdate>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        var clientIp = req.ClientIp ?? "Unknown";

        var validationError = YtDlpArguments.Validate(req, out var uri);
        if (validationError != null || uri == null)
        {
            _logger.LogError(validationError ?? "Invalid request", clientIp, "Download");
            return new DownloadResult { Success = false, ErrorMessage = validationError };
        }

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (_options.TimeoutMinutes > 0)
        {
            linkedCts.CancelAfter(TimeSpan.FromMinutes(_options.TimeoutMinutes));
        }

        if (_limiter.IsSaturated)
        {
            onProgress?.Invoke(new DownloadProgressUpdate
            {
                Percent = 0,
                StatusMessage = "Waiting for another download to finish...",
                IsIndeterminate = true
            });
        }

        bool acquired = false;
        try
        {
            await _limiter.WaitAsync(linkedCts.Token);
            acquired = true;
        }
        catch (OperationCanceledException)
        {
            bool isTimeout = !cancellationToken.IsCancellationRequested && linkedCts.IsCancellationRequested;
            var cancelMsg = isTimeout ? $"Download timed out after {_options.TimeoutMinutes} minutes" : "Download cancelled";
            _logger.LogError(cancelMsg, clientIp, "Download");
            return new DownloadResult { Success = false, ErrorMessage = cancelMsg };
        }

        try
        {
            onProgress?.Invoke(new DownloadProgressUpdate
            {
                Percent = 0,
                StatusMessage = "Starting download process...",
                IsIndeterminate = true
            });

            // Create output template with video title and unique job token
            var jobToken = YtDlpArguments.NewJobToken();
            var outTemplate = YtDlpArguments.OutputTemplate(_downloadsPath, jobToken);

            var denoPath = !string.IsNullOrWhiteSpace(_denoPath) && File.Exists(_denoPath) ? _denoPath : null;
            var args = YtDlpArguments.Build(req, uri, outTemplate, denoPath);

            var ytDlpPath = _options.ResolveExecutablePath();
            var psi = new ProcessStartInfo
            {
                FileName = ytDlpPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            foreach (var arg in args)
            {
                psi.ArgumentList.Add(arg);
            }

            if (!string.IsNullOrWhiteSpace(_denoPath))
            {
                var denoDir = Path.GetDirectoryName(_denoPath);
                if (!string.IsNullOrEmpty(denoDir))
                {
                    var existingPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
                    if (!existingPath.Contains(denoDir, StringComparison.OrdinalIgnoreCase))
                        psi.Environment["PATH"] = $"{denoDir}{Path.PathSeparator}{existingPath}";
                }
            }

            Process proc;
            try
            {
                var started = Process.Start(psi);
                if (started is null)
                {
                    var msg = "Failed to start yt-dlp process";
                    _logger.LogError(msg, clientIp, "Download");
                    return new DownloadResult { Success = false, ErrorMessage = msg };
                }
                proc = started;
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                var msg = $"yt-dlp could not be started at {ytDlpPath}: {ex.Message}";
                _logger.LogError(msg, clientIp, "Download");
                return new DownloadResult { Success = false, ErrorMessage = msg };
            }

            using (proc)
            {
                var stdOutBuilder = new StringBuilder();
                var stdErrBuilder = new StringBuilder();
                var progressParser = new ProgressParser();
                var progressLock = new object();

                var stdOutTask = Task.Run(async () =>
                {
                    try
                    {
                        while (await proc.StandardOutput.ReadLineAsync() is { } line)
                        {
                            stdOutBuilder.AppendLine(line);
                            DownloadProgressUpdate? update;
                            lock (progressLock)
                            {
                                update = progressParser.Parse(line, DateTime.UtcNow);
                            }
                            if (update != null)
                            {
                                onProgress?.Invoke(update);
                            }
                        }
                    }
                    catch { /* Ignore stream reading errors on exit */ }
                });

                var stdErrTask = Task.Run(async () =>
                {
                    try
                    {
                        while (await proc.StandardError.ReadLineAsync() is { } line)
                        {
                            stdErrBuilder.AppendLine(line);
                            DownloadProgressUpdate? update;
                            lock (progressLock)
                            {
                                update = progressParser.Parse(line, DateTime.UtcNow);
                            }
                            if (update != null)
                            {
                                onProgress?.Invoke(update);
                            }
                        }
                    }
                    catch { /* Ignore stream reading errors on exit */ }
                });

                try
                {
                    await proc.WaitForExitAsync(linkedCts.Token);
                    await Task.WhenAll(stdOutTask, stdErrTask);
                }
                catch (OperationCanceledException)
                {
                    try
                    {
                        proc.Kill(entireProcessTree: true);
                    }
                    catch (InvalidOperationException)
                    {
                    }

                    await proc.WaitForExitAsync(CancellationToken.None);
                    await Task.WhenAll(stdOutTask, stdErrTask);

                    // Delete only this job's token-matching files including partials
                    var tokenPattern = $"_{jobToken}.";
                    try
                    {
                        var filesToDelete = Directory.GetFiles(_downloadsPath)
                            .Where(f => Path.GetFileName(f).Contains(tokenPattern, StringComparison.Ordinal));
                        foreach (var f in filesToDelete)
                        {
                            try
                            {
                                File.Delete(f);
                            }
                            catch
                            {
                            }
                        }
                    }
                    catch
                    {
                    }

                    bool isTimeout = !cancellationToken.IsCancellationRequested && linkedCts.IsCancellationRequested;
                    var cancelMsg = isTimeout ? $"Download timed out after {_options.TimeoutMinutes} minutes" : "Download cancelled";
                    _logger.LogError(cancelMsg, clientIp, "Download");
                    return new DownloadResult { Success = false, ErrorMessage = cancelMsg };
                }

                var stdOut = stdOutBuilder.ToString();
                var stdErr = stdErrBuilder.ToString();

                if (proc.ExitCode != 0)
                {
                    if (stdOut.Contains("does not pass filter") || stdErr.Contains("does not pass filter"))
                    {
                        var liveMsg = "Live streams are not supported";
                        _logger.LogError(liveMsg, clientIp, "Download");
                        return new DownloadResult { Success = false, ErrorMessage = liveMsg };
                    }

                    var msg = $"yt-dlp failed: {stdErr}";
                    _logger.LogError(msg, clientIp, "Download");
                    return new DownloadResult { Success = false, ErrorMessage = $"{stdErr}\n{stdOut}" };
                }

                // Find downloaded files by matching the embedded job token
                var located = DownloadOutputLocator.Find(_downloadsPath, jobToken);
                var matchingFile = located.MediaPath;
                var thumbnailFilePath = located.ThumbnailPath;
                var subtitleFiles = located.SubtitlePaths;

                if (string.IsNullOrEmpty(matchingFile))
                {
                    if (stdOut.Contains("does not pass filter") || stdErr.Contains("does not pass filter"))
                    {
                        var liveMsg = "Live streams are not supported";
                        _logger.LogError(liveMsg, clientIp, "Download");
                        return new DownloadResult { Success = false, ErrorMessage = liveMsg };
                    }

                    var tokenPattern = $"_{jobToken}.";
                    var filesInFolder = Directory.GetFiles(_downloadsPath)
                        .Where(f => Path.GetFileName(f).Contains(tokenPattern, StringComparison.Ordinal))
                        .Select(f => Path.GetFileName(f));
                    var msg = $"Could not find downloaded file with job token {jobToken}. Files in folder: {string.Join(" | ", filesInFolder)}";
                    _logger.LogError(msg, clientIp, "Download");
                    return new DownloadResult { Success = false, ErrorMessage = msg };
                }

                // Verify file exists (with brief retry)
                for (int attempt = 0; attempt < 5; attempt++)
                {
                    if (File.Exists(matchingFile)) break;
                    await Task.Delay(100);
                }

                if (!File.Exists(matchingFile))
                {
                    var filesInDir = string.Join(" | ", Directory.GetFiles(_downloadsPath).Select(f => Path.GetFileName(f)));
                    var msg = $"File path returned by yt-dlp does not exist: {matchingFile}. Files in downloads folder: {filesInDir}";
                    _logger.LogError(msg, clientIp, "Download");
                    return new DownloadResult { Success = false, ErrorMessage = msg };
                }

                var fileName = Path.GetFileName(matchingFile);
                var publicUrl = $"downloads/{Uri.EscapeDataString(fileName)}";

                // Handle thumbnail
                string? thumbFileName = null;
                string? thumbPublicUrl = null;
                if (!string.IsNullOrEmpty(thumbnailFilePath) && File.Exists(thumbnailFilePath))
                {
                    thumbFileName = Path.GetFileName(thumbnailFilePath);
                    thumbPublicUrl = $"downloads/{Uri.EscapeDataString(thumbFileName)}";
                }

                // Handle subtitles
                var subtitlesList = subtitleFiles.Select(f =>
                {
                    var subName = Path.GetFileName(f);
                    return new DownloadFileResult
                    {
                        File = subName,
                        Url = $"downloads/{Uri.EscapeDataString(subName)}"
                    };
                }).ToList();

                _logger.LogDownload(fileName, clientIp);

                return new DownloadResult
                {
                    Success = true,
                    Url = publicUrl,
                    File = fileName,
                    ThumbnailUrl = thumbPublicUrl,
                    ThumbnailFile = thumbFileName,
                    Subtitles = subtitlesList
                };
            }
        }
        finally
        {
            if (acquired)
            {
                _limiter.Release();
            }
        }
    }
}

