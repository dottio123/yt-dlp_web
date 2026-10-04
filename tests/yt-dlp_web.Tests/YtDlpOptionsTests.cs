namespace yt_dlp_web.Tests;

using yt_dlp_web.Services;

public class YtDlpOptionsTests
{
    [Fact]
    public void ResolveExecutablePath_UsesConfiguredValue()
    {
        var options = new YtDlpOptions { ExecutablePath = "/opt/yt-dlp" };
        Assert.Equal("/opt/yt-dlp", options.ResolveExecutablePath());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveExecutablePath_DefaultsPerOs(string? configuredPath)
    {
        var options = new YtDlpOptions { ExecutablePath = configuredPath };
        var resolved = options.ResolveExecutablePath();

        if (OperatingSystem.IsWindows())
        {
            var expected = Path.Combine(AppContext.BaseDirectory, "tools", "yt-dlp.exe");
            Assert.Equal(expected, resolved);
        }
        else
        {
            Assert.Equal("/usr/local/bin/yt-dlp", resolved);
        }
    }
}
