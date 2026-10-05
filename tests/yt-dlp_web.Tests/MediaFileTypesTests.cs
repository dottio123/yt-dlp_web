namespace yt_dlp_web.Tests;

using yt_dlp_web.Services;

public class MediaFileTypesTests
{
    [Theory]
    [InlineData("a.OPUS", true)]
    [InlineData("a.mp4", false)]
    public void IsAudio_IsCaseInsensitive(string fileName, bool expected)
    {
        Assert.Equal(expected, MediaFileTypes.IsAudio(fileName));
    }

    [Theory]
    [InlineData("a.mp4.part")]
    [InlineData("a.f137.mp4.ytdl")]
    [InlineData("a.mp4.part-Frag12")]
    public void IsPartial_True(string fileName)
    {
        Assert.True(MediaFileTypes.IsPartial(fileName));
    }

    [Theory]
    [InlineData("t_tok.en.vtt", "en")]
    [InlineData("t_tok.en-US.srt", "en-US")]
    [InlineData("Mr. Robot_tok.vtt", null)]
    [InlineData("t_tok.vtt", null)]
    public void SubtitleLanguage_ReadsLanguageSegment(string name, string? expected)
    {
        Assert.Equal(expected, MediaFileTypes.SubtitleLanguage(name));
    }

    [Fact]
    public void PlayableSubtitlesFor_ReturnsThisVideosVttTracksInOrder()
    {
        var files = new[]
        {
            "A_tok.mp4", "A_tok.en-US.vtt", "A_tok.en-AU.vtt", "A_tok.en.srt", "A_tok.webp",
            "B_tok.en.vtt", "A_tok.extra.en.vtt", "A_tok2.en.vtt"
        };

        var subs = MediaFileTypes.PlayableSubtitlesFor("A_tok.mp4", files);

        Assert.Equal(new[] { "A_tok.en-AU.vtt", "A_tok.en-US.vtt" }, subs);
    }

    [Fact]
    public void PlayableSubtitlesFor_NoTracks_ReturnsEmpty()
    {
        Assert.Empty(MediaFileTypes.PlayableSubtitlesFor("A_tok.mp4", new[] { "A_tok.mp4", "A_tok.webp" }));
    }
}
