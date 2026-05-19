using System.Diagnostics;

namespace yt_dlp_web.Services;

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
    Task<DownloadResult> DownloadAsync(DownloadRequest request);
}

public class DownloadService : IDownloadService
{
    private readonly ILoggingService _logger;
    private readonly string _downloadsPath;
    private readonly string? _denoPath;

    public DownloadService(ILoggingService logger, IWebHostEnvironment env, IConfiguration configuration)
    {
        _logger = logger;
        _downloadsPath = Path.Combine(env.WebRootPath, "downloads");
        Directory.CreateDirectory(_downloadsPath);

        // Read the configured Deno path (may be null/empty when not set)
        _denoPath = configuration["YtDlp:DenoPath"];
    }

    public async Task<DownloadResult> DownloadAsync(DownloadRequest req)
    {
        var clientIp = req.ClientIp ?? "Unknown";

        if (string.IsNullOrWhiteSpace(req.Url))
        {
            var msg = "Missing url";
            _logger.LogError(msg, clientIp, "Download");
            return new DownloadResult { Success = false, ErrorMessage = msg };
        }

        if (!Uri.TryCreate(req.Url, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
        {
            var msg = "Invalid url";
            _logger.LogError(msg, clientIp, "Download");
            return new DownloadResult { Success = false, ErrorMessage = msg };
        }

        // Create output template with video title and timestamp
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var outTemplate = Path.Combine(_downloadsPath, $"%(title)s_{timestamp}.%(ext)s");

        // Build yt-dlp arguments
        var args = new List<string> { "--no-playlist", "-o", outTemplate };

        // Add option to print the final filepath after download
        args.Add("--print");
        args.Add("after_move:filepath");

        if (req.ExtractAudio)
        {
            args.Add("-x");
            args.Add("--audio-format");
            args.Add(req.AudioFormat ?? "mp3");
        }

        if (req.DownloadSubs)
        {
            args.Add("--write-subs");
            args.Add("--sub-langs");
            args.Add(req.SubLangs ?? "en");
        }

        if (req.IncludeThumbnail)
        {
            args.Add("--write-thumbnail");
        }

        if (!string.IsNullOrWhiteSpace(req.Format))
        {
            args.Insert(0, "-f");
            args.Insert(1, req.Format);
        }

        args.Add(req.Url);

        // Inject --js-runtimes when a valid Deno path is configured.
        // yt-dlp searches PATH for "deno" but IIS service accounts typically don't
        // have %USERPROFILE%\.deno\bin on their PATH, so we must be explicit.
        if (!string.IsNullOrWhiteSpace(_denoPath) && File.Exists(_denoPath))
        {
            args.Insert(0, "--js-runtimes");
            args.Insert(1, $"deno:{_denoPath}");
        }

        var ytDlpPath = Path.Combine(AppContext.BaseDirectory, "tools", "yt-dlp.exe");
        var psi = new ProcessStartInfo
        {
            FileName = ytDlpPath,
            Arguments = string.Join(' ', args.Select(a => a.Contains(' ') ? '"' + a + '"' : a)),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        // Belt-and-suspenders: also inject the Deno bin directory into the child
        // process PATH so any indirect runtime discovery by yt-dlp also works.
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

        var stdOutTask = proc.StandardOutput.ReadToEndAsync();
        var stdErrTask = proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync();

        var stdOut = await stdOutTask;
        var stdErr = await stdErrTask;

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
}
