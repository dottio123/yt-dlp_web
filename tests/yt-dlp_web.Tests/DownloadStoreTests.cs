namespace yt_dlp_web.Tests;

using yt_dlp_web.Services;

public class DownloadStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly DownloadStore _store;

    public DownloadStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "yt-dlp_test_" + Guid.NewGuid().ToString("N"));
        _store = new DownloadStore(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    [Fact]
    public void TryResolve_PlainName_ReturnsPathInsideRoot()
    {
        var resolved = _store.TryResolve("video.mp4", out var fullPath);
        Assert.True(resolved);
        Assert.Equal(Path.Combine(_tempDir, "video.mp4"), fullPath);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("../downloads_old/x.mp4")]
    [InlineData("sub/x.mp4")]
    public void TryResolve_RejectsTraversalAndSubpaths(string name)
    {
        var resolved = _store.TryResolve(name, out _);
        Assert.False(resolved);
    }

    [Fact]
    public void TryResolve_RejectsSiblingFolderWithSamePrefix()
    {
        var siblingPath = "../downloads_old";
        var resolved = _store.TryResolve(siblingPath, out _);
        Assert.False(resolved);
    }

    [Fact]
    public void TryResolve_RejectsAbsolutePath()
    {
        var absPath = Path.Combine(Path.GetTempPath(), "x.mp4");
        var resolved = _store.TryResolve(absPath, out _);
        Assert.False(resolved);
    }

    [Theory]
    [InlineData("a%41b.mp4")]
    [InlineData("50% off #1?.mp4")]
    [InlineData("Ünïcødé – title.webm")]
    public void TryResolve_AcceptsUnusualButValidNames(string name)
    {
        if (OperatingSystem.IsWindows() && name.Contains('?'))
            return;

        var resolved = _store.TryResolve(name, out var fullPath);
        Assert.True(resolved);
        Assert.Equal(name, Path.GetFileName(fullPath));
    }

    [Fact]
    public void Delete_InvalidName_ReturnsFalseAndDeletesNothing()
    {
        var keepFile = Path.Combine(_tempDir, "keep.mp4");
        File.WriteAllText(keepFile, "content");

        var deleted = _store.Delete("../keep.mp4");
        Assert.False(deleted);
        Assert.True(File.Exists(keepFile));
    }

    [Fact]
    public void List_ReturnsNewestFirstWithEscapedDownloadUrl()
    {
        var olderFile = Path.Combine(_tempDir, "older.mp4");
        var newerFile = Path.Combine(_tempDir, "a b.mp4");

        File.WriteAllText(olderFile, "older");
        File.WriteAllText(newerFile, "newer");

        File.SetLastWriteTime(olderFile, DateTime.Now.AddMinutes(-10));
        File.SetLastWriteTime(newerFile, DateTime.Now);

        var list = _store.List();
        Assert.Equal(2, list.Count);
        Assert.Equal("a b.mp4", list[0].Name);
        Assert.Equal("download/a%20b.mp4", list[0].DownloadUrl);
        Assert.Equal("older.mp4", list[1].Name);
        Assert.Equal("download/older.mp4", list[1].DownloadUrl);
    }
}
