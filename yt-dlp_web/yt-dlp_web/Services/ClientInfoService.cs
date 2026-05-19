namespace yt_dlp_web.Services;

public interface IClientInfoService
{
    string GetClientIp();
}

public class ClientInfoService : IClientInfoService
{
    private readonly IHttpContextAccessor _contextAccessor;

    public ClientInfoService(IHttpContextAccessor contextAccessor)
    {
        _contextAccessor = contextAccessor;
    }

    public string GetClientIp()
    {
        var context = _contextAccessor.HttpContext;
        if (context is null)
            return "Unknown";

        // Check for X-Forwarded-For header (proxies)
        if (context.Request.Headers.TryGetValue("X-Forwarded-For", out var forwarded))
        {
            var ip = forwarded.ToString().Split(',')[0].Trim();
            if (!string.IsNullOrEmpty(ip))
                return ip;
        }

        // Check for X-Real-IP header
        if (context.Request.Headers.TryGetValue("X-Real-IP", out var realIp))
        {
            var ip = realIp.ToString().Trim();
            if (!string.IsNullOrEmpty(ip))
                return ip;
        }

        // Check for CF-Connecting-IP header (Cloudflare)
        if (context.Request.Headers.TryGetValue("CF-Connecting-IP", out var cfIp))
        {
            var ip = cfIp.ToString().Trim();
            if (!string.IsNullOrEmpty(ip))
                return ip;
        }

        // Fall back to RemoteIpAddress
        return context.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
    }
}
