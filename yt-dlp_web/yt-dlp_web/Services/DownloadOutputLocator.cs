namespace yt_dlp_web.Services;

public sealed record LocatedOutputs(string? MediaPath, string? ThumbnailPath, IReadOnlyList<string> SubtitlePaths);

public static class DownloadOutputLocator
{
    public static LocatedOutputs Find(string downloadsPath, string jobToken)
    {
        if (string.IsNullOrWhiteSpace(downloadsPath) || !Directory.Exists(downloadsPath))
        {
            return new LocatedOutputs(null, null, Array.Empty<string>());
        }

        var tokenPattern = $"_{jobToken}.";
        var matchedFiles = Directory.GetFiles(downloadsPath)
            .Where(f => Path.GetFileName(f).Contains(tokenPattern, StringComparison.Ordinal) && !MediaFileTypes.IsPartial(f))
            .ToList();

        var mediaFiles = matchedFiles
            .Where(f => !MediaFileTypes.IsImage(f) && !MediaFileTypes.IsSubtitle(f))
            .OrderByDescending(f => new FileInfo(f).LastWriteTimeUtc)
            .ToList();

        var thumbnailFiles = matchedFiles
            .Where(MediaFileTypes.IsImage)
            .OrderByDescending(f => new FileInfo(f).LastWriteTimeUtc)
            .ToList();

        var subtitleFiles = matchedFiles
            .Where(MediaFileTypes.IsSubtitle)
            .OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal)
            .ToList();

        return new LocatedOutputs(
            mediaFiles.FirstOrDefault(),
            thumbnailFiles.FirstOrDefault(),
            subtitleFiles
        );
    }
}
