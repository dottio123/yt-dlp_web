using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

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
    Task<DownloadResult> DownloadAsync(DownloadRequest request, Action<DownloadProgressUpdate>? onProgress = null);
}

public class DownloadService : IDownloadService
{
    private readonly ILoggingService _logger;
    private readonly string _downloadsPath;
    private readonly string? _denoPath;

    private static readonly Regex AnsiRegex = new(@"\x1B\[[^@-~]*[@-~]", RegexOptions.Compiled);
    private static readonly Regex ProgressTemplateRegex = new(
        @"^download-progress:\s*([\d\.]+)%\|([^\|]*)\|([^\|]*)\|([^\|\r\n]*)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex PercentRegex = new(@"([\d\.]+)%", RegexOptions.Compiled);
    private static readonly Regex TotalSizeRegex = new(@"of\s+~?\s*([\d\.]+\s*[KMGTP]?i?B)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex SpeedRegex = new(@"at\s+([\d\.]+\s*[KMGTP]?i?B/s|Unknown(?:\s*B/s)?)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex EtaRegex = new(@"ETA\s+([\d\:]+|Unknown)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public DownloadService(ILoggingService logger, IDownloadStore store, IConfiguration configuration)
    {
        _logger = logger;
        _downloadsPath = store.RootPath;

        // Read the configured Deno path (may be null/empty when not set)
        _denoPath = configuration["YtDlp:DenoPath"];
    }

    public async Task<DownloadResult> DownloadAsync(DownloadRequest req, Action<DownloadProgressUpdate>? onProgress = null)
    {
        var clientIp = req.ClientIp ?? "Unknown";

        var validationError = YtDlpArguments.Validate(req, out var uri);
        if (validationError != null || uri == null)
        {
            _logger.LogError(validationError ?? "Invalid request", clientIp, "Download");
            return new DownloadResult { Success = false, ErrorMessage = validationError };
        }

        onProgress?.Invoke(new DownloadProgressUpdate
        {
            Percent = 0,
            StatusMessage = "Starting download process...",
            IsIndeterminate = true
        });

        // Create output template with video title and timestamp
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var outTemplate = Path.Combine(_downloadsPath, $"%(title)s_{timestamp}.%(ext)s");

        var denoPath = !string.IsNullOrWhiteSpace(_denoPath) && File.Exists(_denoPath) ? _denoPath : null;
        var args = YtDlpArguments.Build(req, uri, outTemplate, denoPath);

        var ytDlpPath = "/usr/local/bin/yt-dlp";
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
                    psi.Environment["PATH"] = $"{denoDir};{existingPath}";
            }
        }

        using var proc = Process.Start(psi);
        if (proc is null)
        {
            var msg = "Failed to start yt-dlp process";
            _logger.LogError(msg, clientIp, "Download");
            return new DownloadResult { Success = false, ErrorMessage = msg };
        }

        var stdOutBuilder = new StringBuilder();
        var stdErrBuilder = new StringBuilder();
        var trackingState = new ProgressTrackingState();

        var stdOutTask = Task.Run(async () =>
        {
            try
            {
                while (await proc.StandardOutput.ReadLineAsync() is { } line)
                {
                    stdOutBuilder.AppendLine(line);
                    ParseOutputLine(line, onProgress, trackingState);
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
                    ParseOutputLine(line, onProgress, trackingState);
                }
            }
            catch { /* Ignore stream reading errors on exit */ }
        });

        await Task.WhenAll(stdOutTask, stdErrTask, proc.WaitForExitAsync());

        var stdOut = stdOutBuilder.ToString();
        var stdErr = stdErrBuilder.ToString();

        if (proc.ExitCode != 0)
        {
            var msg = $"yt-dlp failed: {stdErr}";
            _logger.LogError(msg, clientIp, "Download");
            return new DownloadResult { Success = false, ErrorMessage = $"{stdErr}\n{stdOut}" };
        }

        // Find downloaded files by matching the embedded timestamp
        var timestampPattern = $"_{timestamp}";
        var downloadedFiles = Directory.GetFiles(_downloadsPath)
            .Where(f => Path.GetFileName(f).Contains(timestampPattern, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var imageExts = new[] { ".jpg", ".jpeg", ".png", ".webp" };
        var subExts = new[] { ".vtt", ".srt", ".ass", ".ssa" };

        var matchingFile = downloadedFiles
            .Where(f => !imageExts.Contains(Path.GetExtension(f).ToLowerInvariant()) &&
                        !subExts.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .OrderByDescending(f => new FileInfo(f).LastWriteTime)
            .FirstOrDefault();

        var thumbnailFilePath = downloadedFiles
            .Where(f => imageExts.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .OrderByDescending(f => new FileInfo(f).LastWriteTime)
            .FirstOrDefault();

        var subtitleFiles = downloadedFiles
            .Where(f => subExts.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .OrderByDescending(f => new FileInfo(f).Name)
            .ToList();

        if (string.IsNullOrEmpty(matchingFile))
        {
            var msg = $"Could not find downloaded file with timestamp {timestamp}. Files in folder: {string.Join(" | ", downloadedFiles.Select(f => Path.GetFileName(f)))}";
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

    private void ParseOutputLine(string line, Action<DownloadProgressUpdate>? onProgress, ProgressTrackingState state)
    {
        if (onProgress == null || string.IsNullOrWhiteSpace(line)) return;

        var clean = AnsiRegex.Replace(line, "").Trim();
        if (string.IsNullOrWhiteSpace(clean)) return;

        var now = DateTime.UtcNow;

        // 1. Check custom progress template: download-progress:percent%|total|speed|eta
        var templateMatch = ProgressTemplateRegex.Match(clean);
        if (templateMatch.Success)
        {
            if (double.TryParse(templateMatch.Groups[1].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var percent))
            {
                var total = templateMatch.Groups[2].Value.Trim();
                var speed = templateMatch.Groups[3].Value.Trim();
                var eta = templateMatch.Groups[4].Value.Trim();

                if (string.Equals(speed, "NA", StringComparison.OrdinalIgnoreCase) || speed.StartsWith("Unknown", StringComparison.OrdinalIgnoreCase)) speed = null;
                if (string.Equals(eta, "NA", StringComparison.OrdinalIgnoreCase) || eta.StartsWith("Unknown", StringComparison.OrdinalIgnoreCase)) eta = null;
                if (string.Equals(total, "NA", StringComparison.OrdinalIgnoreCase) || total.StartsWith("Unknown", StringComparison.OrdinalIgnoreCase)) total = null;

                // Smooth speed and ETA updates to ~1.2 second intervals to eliminate rapid fluttering
                if (percent >= 100.0)
                {
                    state.StableEta = "00:00";
                }
                else if ((now - state.LastStatsTime).TotalMilliseconds >= 1200 || state.StableSpeed == null)
                {
                    if (!string.IsNullOrEmpty(speed)) state.StableSpeed = speed;
                    if (!string.IsNullOrEmpty(eta)) state.StableEta = eta;
                    state.LastStatsTime = now;
                }

                string? downloaded = null;
                if (!string.IsNullOrEmpty(total))
                {
                    var m = Regex.Match(total, @"([\d\.]+)\s*(\w+)");
                    if (m.Success && double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var tVal))
                    {
                        downloaded = $"{tVal * (percent / 100.0):F1} {m.Groups[2].Value}";
                    }
                }

                // Throttle progress dispatches to prevent UI thrashing while keeping bar animation smooth
                bool shouldEmit = percent >= 100.0 ||
                                  state.LastReportedPercent < 0 ||
                                  Math.Abs(percent - state.LastReportedPercent) >= 0.2 ||
                                  (now - state.LastEmitTime).TotalMilliseconds >= 250;

                if (shouldEmit)
                {
                    state.LastReportedPercent = percent;
                    state.LastEmitTime = now;

                    onProgress.Invoke(new DownloadProgressUpdate
                    {
                        Percent = Math.Clamp(percent, 0.0, 100.0),
                        Speed = state.StableSpeed,
                        Eta = state.StableEta,
                        DownloadedSize = downloaded,
                        TotalSize = total,
                        StatusMessage = percent >= 100.0 ? "Download complete, processing..." : "Downloading media...",
                        IsIndeterminate = false
                    });
                }
                return;
            }
        }

        // 2. Standard [download] line fallback
        if (clean.StartsWith("[download]", StringComparison.OrdinalIgnoreCase))
        {
            if (clean.Contains("Destination:", StringComparison.OrdinalIgnoreCase))
            {
                onProgress.Invoke(new DownloadProgressUpdate
                {
                    Percent = 10,
                    StatusMessage = "Starting media download...",
                    IsIndeterminate = false
                });
                return;
            }

            var pMatch = PercentRegex.Match(clean);
            if (pMatch.Success && double.TryParse(pMatch.Groups[1].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var percent))
            {
                var totMatch = TotalSizeRegex.Match(clean);
                var spMatch = SpeedRegex.Match(clean);
                var etMatch = EtaRegex.Match(clean);

                string? totalSize = totMatch.Success ? totMatch.Groups[1].Value.Trim() : null;
                string? speed = spMatch.Success ? spMatch.Groups[1].Value.Trim() : null;
                string? eta = etMatch.Success ? etMatch.Groups[1].Value.Trim() : null;

                if (speed != null && (string.Equals(speed, "NA", StringComparison.OrdinalIgnoreCase) || speed.StartsWith("Unknown", StringComparison.OrdinalIgnoreCase))) speed = null;
                if (eta != null && (string.Equals(eta, "NA", StringComparison.OrdinalIgnoreCase) || eta.StartsWith("Unknown", StringComparison.OrdinalIgnoreCase))) eta = null;

                if (percent >= 100.0)
                {
                    state.StableEta = "00:00";
                }
                else if ((now - state.LastStatsTime).TotalMilliseconds >= 1200 || state.StableSpeed == null)
                {
                    if (!string.IsNullOrEmpty(speed)) state.StableSpeed = speed;
                    if (!string.IsNullOrEmpty(eta)) state.StableEta = eta;
                    state.LastStatsTime = now;
                }

                string? downloadedSize = null;
                if (!string.IsNullOrEmpty(totalSize))
                {
                    var sizeNumMatch = Regex.Match(totalSize, @"([\d\.]+)\s*(\w+)");
                    if (sizeNumMatch.Success && double.TryParse(sizeNumMatch.Groups[1].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var totalVal))
                    {
                        var downloadedVal = totalVal * (percent / 100.0);
                        downloadedSize = $"{downloadedVal:F1} {sizeNumMatch.Groups[2].Value}";
                    }
                }

                bool shouldEmit = percent >= 100.0 ||
                                  state.LastReportedPercent < 0 ||
                                  Math.Abs(percent - state.LastReportedPercent) >= 0.2 ||
                                  (now - state.LastEmitTime).TotalMilliseconds >= 250;

                if (shouldEmit)
                {
                    state.LastReportedPercent = percent;
                    state.LastEmitTime = now;

                    onProgress.Invoke(new DownloadProgressUpdate
                    {
                        Percent = Math.Clamp(percent, 0.0, 100.0),
                        Speed = state.StableSpeed,
                        Eta = state.StableEta,
                        DownloadedSize = downloadedSize,
                        TotalSize = totalSize,
                        StatusMessage = percent >= 100.0 ? "Download complete, processing..." : "Downloading media...",
                        IsIndeterminate = false
                    });
                }
                return;
            }
        }

        // 3. Stage transitions
        if (clean.StartsWith("[youtube]", StringComparison.OrdinalIgnoreCase) ||
            clean.Contains("Extracting URL", StringComparison.OrdinalIgnoreCase) ||
            clean.Contains("Downloading webpage", StringComparison.OrdinalIgnoreCase))
        {
            onProgress.Invoke(new DownloadProgressUpdate
            {
                Percent = 5,
                StatusMessage = "Fetching video information...",
                IsIndeterminate = false
            });
        }
        else if (clean.StartsWith("[info]", StringComparison.OrdinalIgnoreCase) ||
                 clean.Contains("Downloading 1 format", StringComparison.OrdinalIgnoreCase))
        {
            onProgress.Invoke(new DownloadProgressUpdate
            {
                Percent = 8,
                StatusMessage = "Retrieving media stream...",
                IsIndeterminate = false
            });
        }
        else if (clean.StartsWith("[ExtractAudio]", StringComparison.OrdinalIgnoreCase))
        {
            onProgress.Invoke(new DownloadProgressUpdate
            {
                Percent = 92,
                StatusMessage = "Extracting audio track...",
                IsIndeterminate = false
            });
        }
        else if (clean.StartsWith("[Merger]", StringComparison.OrdinalIgnoreCase) ||
                 clean.Contains("Merging formats", StringComparison.OrdinalIgnoreCase))
        {
            onProgress.Invoke(new DownloadProgressUpdate
            {
                Percent = 95,
                StatusMessage = "Merging video and audio with FFmpeg...",
                IsIndeterminate = false
            });
        }
        else if (clean.StartsWith("[Fixup", StringComparison.OrdinalIgnoreCase) ||
                 clean.StartsWith("[VideoConvertor]", StringComparison.OrdinalIgnoreCase))
        {
            onProgress.Invoke(new DownloadProgressUpdate
            {
                Percent = 97,
                StatusMessage = "Finalizing media file...",
                IsIndeterminate = false
            });
        }
        else if (clean.Contains("Writing video subtitles", StringComparison.OrdinalIgnoreCase) ||
                 clean.Contains("Writing video thumbnail", StringComparison.OrdinalIgnoreCase))
        {
            onProgress.Invoke(new DownloadProgressUpdate
            {
                Percent = 98,
                StatusMessage = "Saving subtitles & thumbnail...",
                IsIndeterminate = false
            });
        }
    }

    private class ProgressTrackingState
    {
        public DateTime LastStatsTime = DateTime.MinValue;
        public string? StableSpeed;
        public string? StableEta;
        public double LastReportedPercent = -1;
        public DateTime LastEmitTime = DateTime.MinValue;
    }
}
