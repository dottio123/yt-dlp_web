namespace yt_dlp_web.Services;

using System.Text.RegularExpressions;

public static partial class YtDlpArguments
{
    public static readonly IReadOnlySet<string> AudioFormats = new HashSet<string>(StringComparer.Ordinal)
    {
        "mp3",
        "m4a",
        "aac"
    };

    public static readonly IReadOnlySet<string> SubtitleLanguages = new HashSet<string>(StringComparer.Ordinal)
    {
        "en",
        "all"
    };

    public const string ProgressTemplate =
        "download-progress:%(progress._percent_str)s|%(progress._total_bytes_str|progress._total_bytes_estimate_str)s|%(progress._speed_str)s|%(progress._eta_str)s";

    [GeneratedRegex(@"^[A-Za-z0-9_+/,.\[\]<>=*:!?-]{1,200}$")]
    private static partial Regex FormatRegex();

    public static string? Validate(DownloadRequest req, out Uri? uri)
    {
        uri = null;

        if (string.IsNullOrWhiteSpace(req.Url))
        {
            return "Missing url";
        }

        if (!Uri.TryCreate(req.Url, UriKind.Absolute, out var parsedUri) ||
            (parsedUri.Scheme != "http" && parsedUri.Scheme != "https"))
        {
            return "Invalid url";
        }

        if (req.AudioFormat != null && !AudioFormats.Contains(req.AudioFormat))
        {
            return "Unsupported audio format";
        }

        if (req.SubLangs != null && !SubtitleLanguages.Contains(req.SubLangs))
        {
            return "Unsupported subtitle language";
        }

        if (!string.IsNullOrEmpty(req.Format))
        {
            if (req.Format.StartsWith('-') || !FormatRegex().IsMatch(req.Format))
            {
                return "Invalid format";
            }
        }

        uri = parsedUri;
        return null;
    }

    public static List<string> Build(DownloadRequest req, Uri uri, string outputTemplate, string? denoPath)
    {
        var args = new List<string>
        {
            "--no-playlist",
            "--progress",
            "--newline",
            "--no-colors",
            "--progress-template",
            ProgressTemplate,
            "-o",
            outputTemplate
        };

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

        if (!string.IsNullOrWhiteSpace(denoPath))
        {
            args.Insert(0, "--js-runtimes");
            args.Insert(1, $"deno:{denoPath}");
        }

        args.Add("--");
        args.Add(uri.AbsoluteUri);

        return args;
    }
}
