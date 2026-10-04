namespace yt_dlp_web.Services;

public sealed class YtDlpOptions
{
    public const string SectionName = "YtDlp";

    public string? ExecutablePath { get; set; }
    public string? DenoPath { get; set; }

    public string ResolveExecutablePath()
    {
        if (!string.IsNullOrWhiteSpace(ExecutablePath))
        {
            return ExecutablePath;
        }

        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(AppContext.BaseDirectory, "tools", "yt-dlp.exe");
        }

        return "/usr/local/bin/yt-dlp";
    }
}
