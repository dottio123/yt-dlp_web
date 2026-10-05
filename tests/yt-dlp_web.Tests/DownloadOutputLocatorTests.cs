namespace yt_dlp_web.Tests;

using yt_dlp_web.Services;

public class DownloadOutputLocatorTests : IDisposable
{
    private readonly string _tempDir;

    public DownloadOutputLocatorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "yt-dlp_locator_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    [Fact]
    public void Find_IgnoresOtherJobsAndPartialFiles()
    {
        var f1 = Path.Combine(_tempDir, "A_tok1.mp4");
        var f2 = Path.Combine(_tempDir, "A_tok1.webp");
        var f3 = Path.Combine(_tempDir, "A_tok1.en.vtt");
        var f4 = Path.Combine(_tempDir, "A_tok1.f137.mp4.part");
        var f5 = Path.Combine(_tempDir, "B_tok2.mp4");
        var f6 = Path.Combine(_tempDir, "A_tok10.mp4");

        File.WriteAllText(f1, "video");
        File.WriteAllText(f2, "thumb");
        File.WriteAllText(f3, "sub");
        File.WriteAllText(f4, "partial");
        File.WriteAllText(f5, "other");
        File.WriteAllText(f6, "other10");

        File.SetLastWriteTimeUtc(f5, DateTime.UtcNow.AddMinutes(5)); // B_tok2 newest

        var located = DownloadOutputLocator.Find(_tempDir, "tok1");

        Assert.NotNull(located.MediaPath);
        Assert.EndsWith("A_tok1.mp4", located.MediaPath);

        Assert.NotNull(located.ThumbnailPath);
        Assert.EndsWith("A_tok1.webp", located.ThumbnailPath);

        Assert.Single(located.SubtitlePaths);
        Assert.EndsWith("A_tok1.en.vtt", located.SubtitlePaths[0]);
    }

    [Fact]
    public void Find_NoMatch_ReturnsNullMedia()
    {
        var located = DownloadOutputLocator.Find(_tempDir, "zzz");
        Assert.Null(located.MediaPath);
        Assert.Null(located.ThumbnailPath);
        Assert.Empty(located.SubtitlePaths);
    }
}
