using System.Text.Json;
using Xunit;
using yt_dlp_web.Services;

namespace yt_dlp_web.Tests;

public class DownloadRequestTests
{
    [Fact]
    public void DownloadRequest_IgnoresClientIpFromJson()
    {
        var json = "{\"url\":\"https://example.com/v\",\"clientIp\":\"6.6.6.6\"}";
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var request = JsonSerializer.Deserialize<DownloadRequest>(json, options);

        Assert.NotNull(request);
        Assert.Equal("https://example.com/v", request.Url);
        Assert.Null(request.ClientIp);
    }
}
