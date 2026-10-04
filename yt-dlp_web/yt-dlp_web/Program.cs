using Microsoft.Extensions.FileProviders;
using Microsoft.AspNetCore.DataProtection;
using yt_dlp_web.Client.Pages;
using yt_dlp_web.Components;
using yt_dlp_web.Services;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Options;
var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents();

builder.Services.AddScoped(sp =>
{
    var navManager = sp.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
    var isDocker = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true";
    // For server-side rendering in Docker, use internal loopback as the external base URI may be inaccessible from inside.
    var baseAddress = isDocker ? "http://localhost:8080/" : navManager.BaseUri;
    return new HttpClient { BaseAddress = new Uri(baseAddress) };
});

builder.Services.AddHttpContextAccessor();

// Configure logging service — persist logs in the mounted config volume
var contentRoot = builder.Environment.ContentRootPath;
var logsPath = Path.Combine(contentRoot, "config", "logs");
Directory.CreateDirectory(logsPath);
builder.Services.AddSingleton<ILoggingService>(new LoggingService(logsPath));

// Configure download store — persist downloads in the mounted config volume
var downloadStore = new DownloadStore(Path.Combine(contentRoot, "config", "downloads"));
builder.Services.AddSingleton<IDownloadStore>(downloadStore);

// Configure Data Protection to persist keys in the mounted config volume
var keysPath = Path.Combine(contentRoot, "config", "keys");
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keysPath))
    .SetApplicationName("ytdlp_web");

// Register client info service
builder.Services.AddScoped<IClientInfoService, ClientInfoService>();

// Configure YtDlp options
builder.Services.Configure<YtDlpOptions>(builder.Configuration.GetSection(YtDlpOptions.SectionName));

// Register download limiter
builder.Services.AddSingleton<DownloadLimiter>();

// Register download service
builder.Services.AddScoped<IDownloadService, DownloadService>();

// Register update service as both hosted service and injectable interface
builder.Services.AddSingleton<IUpdateService, UpdateService>(sp =>
    new UpdateService(
        sp.GetRequiredService<ILoggingService>(),
        sp.GetRequiredService<ILogger<UpdateService>>(),
        sp.GetRequiredService<IOptions<YtDlpOptions>>())
);
builder.Services.AddHostedService(sp => sp.GetRequiredService<IUpdateService>() as UpdateService ?? throw new InvalidOperationException());

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // Clear known networks and proxies so it works behind Docker/Caddy
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

app.UseForwardedHeaders();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // HSTS is handled by Caddy; skip UseHsts() and UseHttpsRedirection() inside the container.
}

// Serve static files normally from wwwroot
app.UseStaticFiles();

// Content type provider for video, audio, and subtitle streaming
var contentTypeProvider = new FileExtensionContentTypeProvider();
contentTypeProvider.Mappings[".mp4"] = "video/mp4";
contentTypeProvider.Mappings[".webm"] = "video/webm";
contentTypeProvider.Mappings[".mkv"] = "video/x-matroska";
contentTypeProvider.Mappings[".mp3"] = "audio/mpeg";
contentTypeProvider.Mappings[".m4a"] = "audio/mp4";
contentTypeProvider.Mappings[".aac"] = "audio/aac";
contentTypeProvider.Mappings[".vtt"] = "text/vtt";
contentTypeProvider.Mappings[".srt"] = "text/plain";
contentTypeProvider.Mappings[".webp"] = "image/webp";

// Serve files from the downloads folder under the request path /downloads
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(downloadStore.RootPath),
    RequestPath = "/downloads",
    ContentTypeProvider = contentTypeProvider
});

app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(yt_dlp_web.Client._Imports).Assembly);

// Minimal API endpoint for /api/download — delegates to IDownloadService
app.MapPost("/api/download", async (DownloadRequest req, IDownloadService downloadService, HttpContext ctx) =>
{
    var result = await downloadService.DownloadAsync(req, cancellationToken: ctx.RequestAborted);
    if (!result.Success)
        return Results.Problem(result.ErrorMessage);

    return Results.Ok(new
    {
        url = result.Url,
        file = result.File,
        thumbnailUrl = result.ThumbnailUrl,
        thumbnailFile = result.ThumbnailFile,
        subtitles = result.Subtitles.Select(s => new { file = s.File, url = s.Url })
    });
});

// Download endpoint that serves files with Content-Disposition: attachment
app.MapGet("/download/{fileName}", (string fileName, IDownloadStore store) =>
{
    try
    {
        if (!store.TryResolve(fileName, out var fullPath))
            return Results.NotFound();

        if (!File.Exists(fullPath))
            return Results.NotFound();

        var stream = File.OpenRead(fullPath);
        var contentType = contentTypeProvider.TryGetContentType(fileName, out var mime) ? mime : "application/octet-stream";
        return Results.File(stream, contentType, fileName, enableRangeProcessing: true);
    }
    catch
    {
        return Results.NotFound();
    }
});

// API endpoint to list all downloads
app.MapGet("/api/downloads", (IDownloadStore store) =>
{
    try
    {
        return Results.Ok(store.List());
    }
    catch
    {
        return Results.Ok(Array.Empty<DownloadFileInfo>());
    }
});

// API endpoint to delete a download
app.MapDelete("/api/downloads/{fileName}", (string fileName, IDownloadStore store) =>
{
    try
    {
        if (!store.Delete(fileName))
            return Results.BadRequest();

        return Results.Ok();
    }
    catch
    {
        return Results.BadRequest();
    }
});

// API endpoint to delete all downloads
app.MapDelete("/api/downloads", (IDownloadStore store) =>
{
    try
    {
        store.DeleteAll();
        return Results.Ok();
    }
    catch
    {
        return Results.BadRequest();
    }
});

// API endpoint to manually trigger yt-dlp update
app.MapPost("/api/update", async (IUpdateService updateService) =>
{
    try
    {
        var result = await updateService.RunUpdate();
        if (result.Success)
        {
            return Results.Ok(new { success = true, message = result.Message, alreadyLatest = result.AlreadyLatest });
        }
        else
        {
            return Results.BadRequest(new { success = false, message = result.Message });
        }
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message);
    }
});

app.Run();

// DownloadRequest is now defined in Services/DownloadService.cs
