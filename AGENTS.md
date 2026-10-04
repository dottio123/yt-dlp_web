# AGENTS.md

Guidance for AI coding agents working in this repository.

## What this is

`yt-dlp_web` is a self-hosted web front-end for [yt-dlp](https://github.com/yt-dlp/yt-dlp).
A user pastes a media URL, the server shells out to the `yt-dlp` binary, and the resulting
file is stored on the server for preview, download, and deletion.

- Stack: ASP.NET Core 8 (`net8.0`), Blazor Web App with **Interactive Server** render mode.
- A `yt-dlp_web.Client` WebAssembly project exists but is effectively unused template code
  (`Counter.razor`); all real pages render on the server.
- No database. All state is files under `<ContentRoot>/config/`.
- No authentication or authorization. Every endpoint and page is anonymous.

## Layout

```
yt-dlp_web.slnx                         solution (server + client + tests)
docker-compose.yml                      single service, host 7022 -> container 8080, ./config mounted
caddy/Caddyfile                         optional reverse proxy
docs/docker-setup.md                    Docker and reverse proxy deployment documentation
Package/                                MSDeploy/IIS package artifacts (v1.2 zips, deploy.cmd)
config/                                 runtime data mounted into the container (keys/, logs/, downloads/)
tests/yt-dlp_web.Tests/                 xUnit test project (arguments, store, locator, parser, limiter, etc.)
yt-dlp_web/yt-dlp_web/                  server project (the app)
  Program.cs                            DI, middleware, and all minimal API endpoints
  Services/DownloadService.cs           orchestrates downloads, process lifecycle, and progress reporting
  Services/DownloadStore.cs             IDownloadStore: safe path traversal validation, file listing, deletion
  Services/DownloadOutputLocator.cs     locates finished download files by unique 8-character job token
  Services/DownloadLimiter.cs           concurrency limiter with queuing and saturation detection
  Services/CircuitConnectionTracker.cs  tracks browser disconnects to abort active downloads after grace period
  Services/ProgressParser.cs            parses yt-dlp stdout/stderr progress templates and lifecycle stages
  Services/MediaFileTypes.cs            centralized file extension checks (audio, video, image, subtitles)
  Services/YtDlpArguments.cs            safe argument builder with allow-list validation
  Services/YtDlpOptions.cs              strongly-typed settings (paths, concurrency, timeouts, grace period)
  Services/UpdateService.cs             BackgroundService: runs yt-dlp update nightly + on demand
  Services/LoggingService.cs            append-only JSONL log with 5 MB rotation (logs.jsonl -> logs.1.jsonl)
  Services/ClientInfoService.cs         client IP from X-Forwarded-For / X-Real-IP / CF-Connecting-IP
  Components/Pages/Home.razor           download form, live progress, result player
  Components/Pages/History.razor        list/play/delete files in config/downloads
  Components/Pages/Logs.razor           log viewer with filters
  Components/MediaPlayerModal.razor     <video>/<audio> preview modal
  Components/AboutModal.razor           about/changelog modal
  Components/Layout/*                   sidebar layout, nav, theme toggle
  wwwroot/theme.js                      dark/light theme persistence (localStorage key yt_dlp_theme)
  wwwroot/app.css                       global styles (CSS variables, dark/light themes)
  Dockerfile, docker-entrypoint.sh      Alpine image; downloads yt-dlp to /usr/local/bin; drops to uid 1000 via su-exec
  web.config, installNotes.txt          legacy IIS hosting
```

## HTTP surface (Program.cs)

| Method | Path | Purpose |
| --- | --- | --- |
| POST | `/api/download` | JSON `DownloadRequest` -> runs yt-dlp, returns file URLs (ignores caller `clientIp`) |
| GET | `/api/downloads` | list files in `config/downloads` |
| DELETE | `/api/downloads/{fileName}` | delete one file |
| DELETE | `/api/downloads` | delete every file |
| POST | `/api/update` | run `yt-dlp -U` |
| GET | `/download/{fileName}` | file with `Content-Disposition: attachment`, range enabled |
| GET | `/downloads/*` | static file serving of `config/downloads` (inline playback) |

Pages inject services directly; the `/api/*` endpoints are for external callers.

## Build and run

```bash
# build
dotnet build yt-dlp_web.slnx

# test
dotnet test tests/yt-dlp_web.Tests/yt-dlp_web.Tests.csproj

# test fallback (when Windows App Control blocks the test host from loading local unsigned DLLs)
docker run --rm -v "${PWD}:/host:ro" mcr.microsoft.com/dotnet/sdk:8.0-alpine sh -c \
  "mkdir -p /work && cp -r /host/yt-dlp_web /host/tests /host/yt-dlp_web.slnx /work/ && \
   rm -rf /work/*/bin /work/*/obj /work/*/*/bin /work/*/*/obj && \
   dotnet test /work/tests/yt-dlp_web.Tests/yt-dlp_web.Tests.csproj"

# container (recommended; this is the supported deployment as of v1.2)
docker compose up --build -d        # http://localhost:7022
```

### Settings (`YtDlp` Section / Environment Variables)

- `MaxConcurrentDownloads` (`YtDlp__MaxConcurrentDownloads`): default `4`. Extra requests wait in queue. Set to `0` for unlimited.
- `TimeoutMinutes` (`YtDlp__TimeoutMinutes`): default `180`. Kills active process tree if exceeded. Set to `0` for unlimited.
- `DisconnectGraceSeconds` (`YtDlp__DisconnectGraceSeconds`): default `30`. Cancels active download if browser tab disconnects for longer than this duration.
- `ExecutablePath` (`YtDlp__ExecutablePath`): full path to yt-dlp executable. Defaults to `/usr/local/bin/yt-dlp` in container and `tools/yt-dlp.exe` on Windows.
- `DenoPath` (`YtDlp__DenoPath`): optional path to Deno binary or directory containing deno, prepended to PATH for yt-dlp.
- Kestrel listens on `http://0.0.0.0:8080` (appsettings + `ASPNETCORE_URLS`). HTTPS/HSTS are expected to be terminated by a reverse proxy.

## Conventions

- C# with nullable reference types and implicit usings enabled; file-scoped namespaces
  (`namespace yt_dlp_web.Services;`).
- Services are interface + implementation pairs registered in `Program.cs`
  (`IDownloadService` scoped, `IDownloadStore` singleton, `ILoggingService` singleton, `IUpdateService` singleton + hosted service).
- Pages declare `@rendermode InteractiveServer` individually; `App.razor`/`Routes.razor` are static.
- Styling uses CSS variables in `wwwroot/app.css` (`--accent-red`, `--bg-card-secondary`, ...) and
  Bootstrap utilities; icons are inline SVG. Keep both dark and light themes working.
- All persistent data lives under `config/` (`keys/`, `logs/`, `downloads/`). Do not write to `wwwroot`.
- Log user-visible events through `ILoggingService` (`LogDownload`, `LogUpdate`, `LogError` with a
  `relatedType` of `"Download"` or `"Update"` so the Logs page filters work).

## Rules for changes

- **Never build a process command line by string concatenation.** Use
  `ProcessStartInfo.ArgumentList`, and pass user input only as values of options you control. Put
  `--` before the URL. Validate `Format`, `AudioFormat`, and `SubLangs` against an allow-list.
- Any endpoint that takes a file name must resolve it with `Path.GetFullPath` and check it is inside
  the downloads directory **including the trailing directory separator**; reuse one helper rather than
  copying the check (`IDownloadStore.TryResolve`).
- Do not commit anything under `config/` (Data Protection keys, logs, downloads). The repo currently
  contains a committed key in `config/keys/`; treat it as compromised.
- Do not add new loopback `HttpClient` calls from server components; inject the service instead.
- Keep file-type lists (audio/video/image/subtitle extensions) in one shared place; use `MediaFileTypes`.
- Do not commit build outputs or the `Package/*.zip` artifacts when changing code.
- Edit files directly on disk; check git status after committing (stale editor buffers have reverted committed files before).

## Known issues (see code review)

- No authentication or authorization, by design. The app must be protected by a reverse proxy or kept on a private network.
- `X-Forwarded-For` can be faked when the app is exposed directly without a trusted reverse proxy.
- The `yt-dlp_web.Client` WebAssembly project is unused template code.
