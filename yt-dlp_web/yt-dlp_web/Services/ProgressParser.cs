namespace yt_dlp_web.Services;

using System.Globalization;
using System.Text.RegularExpressions;

public sealed class ProgressParser
{
    public const string Template =
        "download-progress:%(progress._percent_str)s|%(progress._total_bytes_str)s|%(progress._total_bytes_estimate_str)s|%(progress._speed_str)s|%(progress._eta_str)s";

    private static readonly Regex AnsiRegex = new(@"\x1B\[[^@-~]*[@-~]", RegexOptions.Compiled);
    private static readonly Regex SizeRegex = new(@"^([\d\.]+)\s*(\w+)", RegexOptions.Compiled);

    private DateTime _lastStatsTime = DateTime.MinValue;
    private string? _stableSpeed;
    private string? _stableEta;
    private double _lastReportedPercent = -1;
    private DateTime _lastEmitTime = DateTime.MinValue;

    public DownloadProgressUpdate? Parse(string line, DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;

        var clean = AnsiRegex.Replace(line, "").Trim();
        if (string.IsNullOrWhiteSpace(clean)) return null;

        if (clean.StartsWith("download-progress:", StringComparison.OrdinalIgnoreCase))
        {
            var rawParts = clean.Substring("download-progress:".Length).Split('|');
            if (rawParts.Length >= 5)
            {
                var percentStr = rawParts[0].Trim().TrimEnd('%');
                if (double.TryParse(percentStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var percent))
                {
                    var total = CleanValue(rawParts[1]);
                    var estimate = CleanValue(rawParts[2]);
                    var effectiveTotal = total ?? estimate;

                    var speed = CleanValue(rawParts[3]);
                    var eta = CleanValue(rawParts[4]);

                    // Smooth speed and ETA updates to ~1.2 second intervals to eliminate rapid fluttering
                    if (percent >= 100.0)
                    {
                        _stableEta = "00:00";
                    }
                    else if (_lastStatsTime == DateTime.MinValue || (utcNow - _lastStatsTime).TotalMilliseconds >= 1200 || _stableSpeed == null)
                    {
                        if (!string.IsNullOrEmpty(speed)) _stableSpeed = speed;
                        if (!string.IsNullOrEmpty(eta)) _stableEta = eta;
                        _lastStatsTime = utcNow;
                    }

                    string? downloadedSize = null;
                    if (!string.IsNullOrEmpty(effectiveTotal))
                    {
                        var m = SizeRegex.Match(effectiveTotal);
                        if (m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var tVal))
                        {
                            var downloadedVal = tVal * (percent / 100.0);
                            downloadedSize = downloadedVal.ToString("F1", CultureInfo.InvariantCulture) + " " + m.Groups[2].Value;
                        }
                    }

                    // Throttle progress dispatches to prevent UI thrashing while keeping bar animation smooth
                    bool shouldEmit = percent >= 100.0 ||
                                      _lastReportedPercent < 0 ||
                                      Math.Abs(percent - _lastReportedPercent) >= 0.2 ||
                                      (_lastEmitTime != DateTime.MinValue && (utcNow - _lastEmitTime).TotalMilliseconds >= 250);

                    if (shouldEmit)
                    {
                        _lastReportedPercent = percent;
                        _lastEmitTime = utcNow;

                        return new DownloadProgressUpdate
                        {
                            Percent = Math.Clamp(percent, 0.0, 100.0),
                            Speed = _stableSpeed,
                            Eta = _stableEta,
                            DownloadedSize = downloadedSize,
                            TotalSize = effectiveTotal,
                            StatusMessage = percent >= 100.0 ? "Download complete, processing..." : "Downloading media...",
                            IsIndeterminate = false
                        };
                    }

                    return null;
                }
            }
        }

        // Stage transitions
        if (clean.StartsWith("[download]", StringComparison.OrdinalIgnoreCase))
        {
            if (clean.Contains("Destination:", StringComparison.OrdinalIgnoreCase))
            {
                return new DownloadProgressUpdate
                {
                    Percent = 10,
                    StatusMessage = "Starting media download...",
                    IsIndeterminate = false
                };
            }
            return null;
        }

        if (clean.StartsWith("[youtube]", StringComparison.OrdinalIgnoreCase) ||
            clean.Contains("Extracting URL", StringComparison.OrdinalIgnoreCase) ||
            clean.Contains("Downloading webpage", StringComparison.OrdinalIgnoreCase))
        {
            return new DownloadProgressUpdate
            {
                Percent = 5,
                StatusMessage = "Fetching video information...",
                IsIndeterminate = false
            };
        }

        if (clean.StartsWith("[info]", StringComparison.OrdinalIgnoreCase) ||
            clean.Contains("Downloading 1 format", StringComparison.OrdinalIgnoreCase))
        {
            return new DownloadProgressUpdate
            {
                Percent = 8,
                StatusMessage = "Retrieving media stream...",
                IsIndeterminate = false
            };
        }

        if (clean.StartsWith("[ExtractAudio]", StringComparison.OrdinalIgnoreCase))
        {
            return new DownloadProgressUpdate
            {
                Percent = 92,
                StatusMessage = "Extracting audio track...",
                IsIndeterminate = false
            };
        }

        if (clean.StartsWith("[Merger]", StringComparison.OrdinalIgnoreCase) ||
            clean.Contains("Merging formats", StringComparison.OrdinalIgnoreCase))
        {
            return new DownloadProgressUpdate
            {
                Percent = 95,
                StatusMessage = "Merging video and audio with FFmpeg...",
                IsIndeterminate = false
            };
        }

        if (clean.StartsWith("[Fixup", StringComparison.OrdinalIgnoreCase) ||
            clean.StartsWith("[VideoConvertor]", StringComparison.OrdinalIgnoreCase))
        {
            return new DownloadProgressUpdate
            {
                Percent = 97,
                StatusMessage = "Finalizing media file...",
                IsIndeterminate = false
            };
        }

        if (clean.Contains("Writing video subtitles", StringComparison.OrdinalIgnoreCase) ||
            clean.Contains("Writing video thumbnail", StringComparison.OrdinalIgnoreCase))
        {
            return new DownloadProgressUpdate
            {
                Percent = 98,
                StatusMessage = "Saving subtitles & thumbnail...",
                IsIndeterminate = false
            };
        }

        return null;
    }

    private static string? CleanValue(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var trimmed = raw.Trim();
        if (string.Equals(trimmed, "N/A", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "NA", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("Unknown", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        return trimmed;
    }
}
