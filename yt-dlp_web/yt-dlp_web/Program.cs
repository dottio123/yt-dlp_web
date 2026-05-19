using Microsoft.Extensions.FileProviders;
using yt_dlp_web.Client.Pages;
using yt_dlp_web.Components;
using yt_dlp_web.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents();

builder.Services.AddScoped(sp =>
{
    var navManager = sp.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
    return new HttpClient { BaseAddress = new Uri(navManager.BaseUri) };
});

builder.Services.AddHttpContextAccessor();

// Configure logging service
var contentRoot = builder.Environment.ContentRootPath;
var logsPath = Path.Combine(contentRoot, "logs");
builder.Services.AddSingleton<ILoggingService>(new LoggingService(logsPath));

// Register client info service
builder.Services.AddScoped<IClientInfoService, ClientInfoService>();

// Register download service
builder.Services.AddScoped<IDownloadService, DownloadService>();

// Register update service as both hosted service and injectable interface
builder.Services.AddSingleton<IUpdateService, UpdateService>(sp =>
    new UpdateService(sp.GetRequiredService<ILoggingService>(), sp.GetRequiredService<ILogger<UpdateService>>())
);
builder.Services.AddHostedService(sp => sp.GetRequiredService<IUpdateService>() as UpdateService ?? throw new InvalidOperationException());

var app = builder.Build();

// Downloads directory under the web root (wwwroot/downloads)
var downloadsRel = "downloads";
var wwwroot = app.Environment.WebRootPath;
var downloadsPath = Path.Combine(wwwroot, downloadsRel);
Directory.CreateDirectory(downloadsPath);

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

// Serve static files normally from wwwroot
app.UseStaticFiles();

// Serve files from the downloads folder under the request path /downloads
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(downloadsPath),
    RequestPath = "/downloads"
});

app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(yt_dlp_web.Client._Imports).Assembly);

// Minimal API endpoint for /api/download — delegates to IDownloadService
app.MapPost("/api/download", async (DownloadRequest req, IDownloadService downloadService) =>
{
    var result = await downloadService.DownloadAsync(req);
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
app.MapGet("/download/{fileName}", (string fileName) =>
{
    try
    {
        var decodedFileName = Uri.UnescapeDataString(fileName);
        var filePath = Path.Combine(downloadsPath, decodedFileName);

        // Security: ensure the file is within the downloads folder
        var fullDownloadsPath = Path.GetFullPath(downloadsPath);
        var fullFilePath = Path.GetFullPath(filePath);
        if (!fullFilePath.StartsWith(fullDownloadsPath, StringComparison.OrdinalIgnoreCase))
            return Results.NotFound();

        if (!File.Exists(fullFilePath))
            return Results.NotFound();

        var stream = File.OpenRead(fullFilePath);
        var contentType = "application/octet-stream";
        return Results.File(stream, contentType, decodedFileName, enableRangeProcessing: true);
    }
    catch
    {
        return Results.NotFound();
    }
});

// API endpoint to list all downloads
app.MapGet("/api/downloads", () =>
{
    try
    {
        var files = Directory.GetFiles(downloadsPath)
            .Select(f =>
            {
                var fi = new FileInfo(f);
                return new
                {
                    name = fi.Name,
                    size = fi.Length,
                    modified = fi.LastWriteTime,
                    downloadUrl = $"download/{Uri.EscapeDataString(fi.Name)}"
                };
            })
            .OrderByDescending(f => f.modified)
            .ToList();

        return Results.Ok(files);
    }
    catch
    {
        return Results.Ok(new List<object>());
    }
});

// API endpoint to delete a download
app.MapDelete("/api/downloads/{fileName}", (string fileName) =>
{
    try
    {
        var decodedFileName = Uri.UnescapeDataString(fileName);
        var filePath = Path.Combine(downloadsPath, decodedFileName);

        // Security: ensure the file is within the downloads folder
        var fullDownloadsPath = Path.GetFullPath(downloadsPath);
        var fullFilePath = Path.GetFullPath(filePath);
        if (!fullFilePath.StartsWith(fullDownloadsPath, StringComparison.OrdinalIgnoreCase))
            return Results.BadRequest();

        if (File.Exists(fullFilePath))
            File.Delete(fullFilePath);

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
