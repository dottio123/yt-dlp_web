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
}
