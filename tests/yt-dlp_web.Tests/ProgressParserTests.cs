namespace yt_dlp_web.Tests;

using System.Globalization;
using yt_dlp_web.Services;

public class ProgressParserTests
{
    private readonly DateTime _t0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Parse_UsesEstimateWhenTotalIsNA()
    {
        var parser = new ProgressParser();
        var update = parser.Parse("download-progress: 50.0%|       N/A|  10.00MiB|  1.00MiB/s|00:05", _t0);

        Assert.NotNull(update);
        Assert.Equal(50.0, update.Percent);
        Assert.Equal("10.00MiB", update.TotalSize);
        Assert.Equal("5.0 MiB", update.DownloadedSize);
        Assert.Equal("1.00MiB/s", update.Speed);
        Assert.Equal("00:05", update.Eta);
        Assert.False(update.IsIndeterminate);
    }

    [Fact]
    public void Parse_PrefersExactTotal()
    {
        var parser = new ProgressParser();
        var update = parser.Parse("download-progress: 25.0%|  20.00MiB|  19.00MiB|  1.00MiB/s|00:05", _t0);

        Assert.NotNull(update);
        Assert.Equal(25.0, update.Percent);
        Assert.Equal("20.00MiB", update.TotalSize);
        Assert.Equal("5.0 MiB", update.DownloadedSize);
    }

    [Fact]
    public void Parse_AllSizesMissing()
    {
        var parser = new ProgressParser();
        var update = parser.Parse("download-progress: 10.0%|       N/A|       N/A|  1.00MiB/s|00:05", _t0);

        Assert.NotNull(update);
        Assert.Null(update.TotalSize);
        Assert.Null(update.DownloadedSize);
    }

    [Fact]
    public void Parse_UnknownSpeedIsNull()
    {
        var parser = new ProgressParser();
        var update = parser.Parse("download-progress: 10.0%|       N/A|  10.00MiB|Unknown B/s|NA", _t0);

        Assert.NotNull(update);
        Assert.Null(update.Speed);
        Assert.Null(update.Eta);
    }

    [Fact]
    public void Parse_ThrottlesSmallFastChanges()
    {
        var parser = new ProgressParser();

        var update1 = parser.Parse("download-progress: 10.0%|       N/A|  10.00MiB|  1.00MiB/s|00:05", _t0);
        Assert.NotNull(update1);

        var update2 = parser.Parse("download-progress: 10.1%|       N/A|  10.00MiB|  1.00MiB/s|00:05", _t0.AddMilliseconds(100));
        Assert.Null(update2);

        var update3 = parser.Parse("download-progress:100.0%|       N/A|  10.00MiB|  1.00MiB/s|00:05", _t0.AddMilliseconds(150));
        Assert.NotNull(update3);
        Assert.Equal("00:00", update3.Eta);
    }

    [Fact]
    public void Parse_StageLines()
    {
        var parser = new ProgressParser();

        var updateMerger = parser.Parse("[Merger] Merging formats into \"x.mkv\"", _t0);
        Assert.NotNull(updateMerger);
        Assert.Equal(95.0, updateMerger.Percent);

        var updateExtract = parser.Parse("[ExtractAudio] Destination: x.mp3", _t0);
        Assert.NotNull(updateExtract);
        Assert.Equal(92.0, updateExtract.Percent);
    }

    [Fact]
    public void Parse_UsesInvariantCulture()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var parser = new ProgressParser();
            var update = parser.Parse("download-progress: 50.0%|       N/A|  10.00MiB|  1.00MiB/s|00:05", _t0);

            Assert.NotNull(update);
            Assert.Equal("5.0 MiB", update.DownloadedSize);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }
}
