namespace yt_dlp_web.Services;

using System.Text.RegularExpressions;

public static partial class MediaFileTypes
{
    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".m4a", ".aac", ".wav", ".flac", ".ogg", ".opus"
    };

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".webm", ".mkv", ".mov", ".avi", ".flv", ".m4v"
    };

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp"
    };

    private static readonly HashSet<string> SubtitleExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".vtt", ".srt", ".ass", ".ssa", ".sub", ".lrc"
    };

    private static readonly HashSet<string> PartialExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".part", ".ytdl", ".temp"
    };

    [GeneratedRegex(@"^[a-zA-Z]{2,3}(-[a-zA-Z0-9]+)*$")]
    private static partial Regex SubtitleLanguageRegex();

    public static bool IsAudio(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return false;
        return AudioExtensions.Contains(Path.GetExtension(fileName));
    }

    public static bool IsVideo(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return false;
        return VideoExtensions.Contains(Path.GetExtension(fileName));
    }

    public static bool IsImage(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return false;
        return ImageExtensions.Contains(Path.GetExtension(fileName));
    }

    public static bool IsSubtitle(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return false;
        return SubtitleExtensions.Contains(Path.GetExtension(fileName));
    }

    public static bool IsPartial(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return false;
        var ext = Path.GetExtension(fileName);
        if (PartialExtensions.Contains(ext)) return true;
        return fileName.Contains(".part-Frag", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsPlayable(string fileName) => IsAudio(fileName) || IsVideo(fileName);

    public static string? SubtitleLanguage(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || !IsSubtitle(fileName))
            return null;

        var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
        var lastDot = nameWithoutExt.LastIndexOf('.');
        if (lastDot < 0)
            return null;

        var langCandidate = nameWithoutExt.Substring(lastDot + 1);
        return SubtitleLanguageRegex().IsMatch(langCandidate) ? langCandidate : null;
    }
}
