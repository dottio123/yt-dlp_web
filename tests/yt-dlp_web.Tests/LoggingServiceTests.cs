using System.Text.Json;
using yt_dlp_web.Services;

namespace yt_dlp_web.Tests;

public class LoggingServiceTests : IDisposable
{
    private readonly string _tempDir;

    public LoggingServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "yt_dlp_web_test_logs_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try
            {
                Directory.Delete(_tempDir, recursive: true);
            }
            catch
            {
            }
        }
    }

    [Fact]
    public void GetLogs_SkipsMalformedLines()
    {
        var service = new LoggingService(_tempDir);
        service.LogDownload("a.mp4", "1.1.1.1");

        var currentFile = Path.Combine(_tempDir, "logs.jsonl");
        File.AppendAllText(currentFile, "{\"Timestamp\":\"2026-\n");

        service.LogDownload("b.mp4", "1.1.1.1");

        var logs = service.GetLogs();
        Assert.Equal(2, logs.Count);
        Assert.Equal("a.mp4", logs[0].FileName);
        Assert.Equal("b.mp4", logs[1].FileName);
    }

    [Fact]
    public void SaveEntry_RotatesWhenFileExceedsLimit()
    {
        var service = new LoggingService(_tempDir, maxFileBytes: 300);

        for (int i = 0; i < 20; i++)
        {
            service.LogError($"e{i}");
        }

        var previousFile = Path.Combine(_tempDir, "logs.1.jsonl");
        var currentFile = Path.Combine(_tempDir, "logs.jsonl");

        Assert.True(File.Exists(previousFile));
        Assert.True(File.Exists(currentFile));
        Assert.True(new FileInfo(currentFile).Length < 600);

        var logs = service.GetLogs(limit: 50);
        Assert.Equal("e19", logs.Last().Message);

        for (int i = 1; i < logs.Count; i++)
        {
            Assert.True(logs[i].Timestamp >= logs[i - 1].Timestamp);
        }
    }

    [Fact]
    public void GetDownloadLogs_FiltersAcrossBothFiles()
    {
        // 400 bytes limit ensures exactly one rotation occurs during this test
        var service = new LoggingService(_tempDir, maxFileBytes: 400);

        service.LogDownload("first.mp4", "1.1.1.1");
        service.LogError("err 1", "1.1.1.1", relatedType: "Download");
        service.LogUpdate("Update checked", success: true);

        // Rotation occurs on or before this append
        service.LogDownload("second.mp4", "2.2.2.2");
        service.LogError("unrelated error");
        service.LogError("err 2", "2.2.2.2", relatedType: "Download");

        var previousFile = Path.Combine(_tempDir, "logs.1.jsonl");
        var currentFile = Path.Combine(_tempDir, "logs.jsonl");

        Assert.True(File.Exists(previousFile), "logs.1.jsonl should exist after rotation");
        Assert.True(File.Exists(currentFile), "logs.jsonl should exist");

        var downloadLogs = service.GetDownloadLogs(limit: 50);

        Assert.Equal(4, downloadLogs.Count);
        Assert.Equal("Downloaded: first.mp4", downloadLogs[0].Message);
        Assert.Equal("err 1", downloadLogs[1].Message);
        Assert.Equal("Downloaded: second.mp4", downloadLogs[2].Message);
        Assert.Equal("err 2", downloadLogs[3].Message);

        for (int i = 1; i < downloadLogs.Count; i++)
        {
            Assert.True(downloadLogs[i].Timestamp >= downloadLogs[i - 1].Timestamp);
        }
    }
}
