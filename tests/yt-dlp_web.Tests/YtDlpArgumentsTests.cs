namespace yt_dlp_web.Tests;

using yt_dlp_web.Services;

public class YtDlpArgumentsTests
{
    [Fact]
    public void Validate_MaliciousUrl_IsEscapedIntoASingleArgument()
    {
        var req = new DownloadRequest { Url = "https://youtu.be/x\" --exec \"before_dl:sh -c 'id'\"" };
        Assert.Null(YtDlpArguments.Validate(req, out var uri));
        var args = YtDlpArguments.Build(req, uri!, "/dl/%(title)s.%(ext)s", null);
        Assert.DoesNotContain("--exec", args);
        Assert.Equal("--", args[^2]);
        Assert.Equal("https://youtu.be/x%22%20--exec%20%22before_dl:sh%20-c%20'id'%22", args[^1]);
    }

    [Theory]
    [InlineData(null, "Missing url")]
    [InlineData("", "Missing url")]
    [InlineData("ftp://x/y", "Invalid url")]
    [InlineData("not a url", "Invalid url")]
    public void Validate_RejectsMissingOrNonHttpUrl(string? url, string expectedError)
    {
        var req = new DownloadRequest { Url = url };
        var error = YtDlpArguments.Validate(req, out var uri);
        Assert.Equal(expectedError, error);
        Assert.Null(uri);
    }

    [Fact]
    public void Validate_RejectsUnknownAudioFormat()
    {
        var req = new DownloadRequest
        {
            Url = "https://example.com/video",
            AudioFormat = "mp3 --exec x"
        };
        var error = YtDlpArguments.Validate(req, out _);
        Assert.Equal("Unsupported audio format", error);
    }

    [Fact]
    public void Validate_RejectsUnknownSubtitleLanguage()
    {
        var req = new DownloadRequest
        {
            Url = "https://example.com/video",
            SubLangs = "en,--exec"
        };
        var error = YtDlpArguments.Validate(req, out _);
        Assert.Equal("Unsupported subtitle language", error);
    }

    [Theory]
    [InlineData("--exec")]
    [InlineData("best video")]
    [InlineData("b\"")]
    public void Validate_RejectsBadFormat(string format)
    {
        var req = new DownloadRequest
        {
            Url = "https://example.com/video",
            Format = format
        };
        var error = YtDlpArguments.Validate(req, out _);
        Assert.Equal("Invalid format", error);
    }

    [Fact]
    public void Validate_AcceptsTypicalFormatSelector()
    {
        var req = new DownloadRequest
        {
            Url = "https://example.com/video",
            Format = "bv*[height<=1080]+ba/b"
        };
        var error = YtDlpArguments.Validate(req, out var uri);
        Assert.Null(error);
        Assert.NotNull(uri);
    }

    [Fact]
    public void Build_ExtractAudio_AddsAudioOptions()
    {
        var req = new DownloadRequest
        {
            Url = "https://example.com/video",
            ExtractAudio = true,
            AudioFormat = "m4a"
        };
        Assert.Null(YtDlpArguments.Validate(req, out var uri));
        var args = YtDlpArguments.Build(req, uri!, "/dl/%(title)s.%(ext)s", null);
        var xIndex = args.IndexOf("-x");
        Assert.True(xIndex >= 0);
        Assert.Equal("--audio-format", args[xIndex + 1]);
        Assert.Equal("m4a", args[xIndex + 2]);
    }

    [Fact]
    public void Build_DefaultsAudioAndSubtitleValues()
    {
        var req = new DownloadRequest
        {
            Url = "https://example.com/video",
            ExtractAudio = true,
            AudioFormat = null,
            DownloadSubs = true,
            SubLangs = null
        };
        Assert.Null(YtDlpArguments.Validate(req, out var uri));
        var args = YtDlpArguments.Build(req, uri!, "/dl/%(title)s.%(ext)s", null);

        var audioIndex = args.IndexOf("--audio-format");
        Assert.True(audioIndex >= 0);
        Assert.Equal("mp3", args[audioIndex + 1]);

        var subsIndex = args.IndexOf("--sub-langs");
        Assert.True(subsIndex >= 0);
        Assert.Equal("en.*", args[subsIndex + 1]);
    }

    [Theory]
    [InlineData("en", "en.*")]
    [InlineData("all", "all")]
    public void Build_MapsSubtitleLanguages(string subLangs, string expected)
    {
        var req = new DownloadRequest
        {
            Url = "https://example.com/video",
            DownloadSubs = true,
            SubLangs = subLangs
        };
        Assert.Null(YtDlpArguments.Validate(req, out var uri));
        var args = YtDlpArguments.Build(req, uri!, "/dl/%(title)s.%(ext)s", null);

        var subsIndex = args.IndexOf("--sub-langs");
        Assert.True(subsIndex >= 0);
        Assert.Equal(expected, args[subsIndex + 1]);
    }

    [Fact]
    public void Build_ChoosesJsRuntime()
    {
        var req = new DownloadRequest
        {
            Url = "https://example.com/video"
        };
        Assert.Null(YtDlpArguments.Validate(req, out var uri));

        var argsWithDeno = YtDlpArguments.Build(req, uri!, "/dl/%(title)s.%(ext)s", "/d/deno");
        var denoIndex = argsWithDeno.IndexOf("--js-runtimes");
        Assert.True(denoIndex >= 0);
        Assert.Equal("deno:/d/deno", argsWithDeno[denoIndex + 1]);

        var argsWithoutDeno = YtDlpArguments.Build(req, uri!, "/dl/%(title)s.%(ext)s", null);
        var nodeIndex = argsWithoutDeno.IndexOf("--js-runtimes");
        Assert.True(nodeIndex >= 0);
        Assert.Equal("node", argsWithoutDeno[nodeIndex + 1]);
        Assert.Single(argsWithoutDeno, a => a == "--js-runtimes");
    }

    [Fact]
    public void OutputTemplate_TruncatesTitle()
    {
        var token = "20261004_120000_12345678";
        var template = YtDlpArguments.OutputTemplate("/downloads", token);
        Assert.Contains("%(title).150B_" + token + ".%(ext)s", template);
    }

    [Fact]
    public void NewJobToken_IsUniqueWithinOneSecond()
    {
        var tokens = new HashSet<string>();
        for (int i = 0; i < 1000; i++)
        {
            tokens.Add(YtDlpArguments.NewJobToken());
        }
        Assert.Equal(1000, tokens.Count);
    }

    [Fact]
    public void Build_DisablesPlaylistsAndLiveStreams()
    {
        var req = new DownloadRequest { Url = "https://example.com/video" };
        Assert.Null(YtDlpArguments.Validate(req, out var uri));
        var args = YtDlpArguments.Build(req, uri!, "/dl/%(title)s.%(ext)s", null);

        Assert.Contains("--no-playlist", args);

        var matchFilterIndex = args.IndexOf("--match-filter");
        Assert.True(matchFilterIndex >= 0);
        Assert.Equal("!is_live", args[matchFilterIndex + 1]);

        var dashDashIndex = args.IndexOf("--");
        Assert.True(matchFilterIndex < dashDashIndex);
    }
}
