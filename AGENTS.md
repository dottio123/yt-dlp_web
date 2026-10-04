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
yt-dlp_web.slnx                         solution (server + client)
docker-compose.yml                      single service, host 7022 -> container 8080, ./config mounted
caddy/Caddyfile                         optional reverse proxy (currently out of sync, see Known issues)
docs/docker-and-pwa-setup.md            older Docker/PWA notes (partly stale)
Package/                                MSDeploy/IIS package artifacts (v1.2 zips, deploy.cmd)
config/                                 runtime data mounted into the container (keys/, logs/, downloads/)
yt-dlp_web/yt-dlp_web/                  server project (the app)
  Program.cs                            DI, middleware, and all minimal API endpoints
  Services/DownloadService.cs           builds yt-dlp args, runs the process, parses progress, finds output files
  Services/UpdateService.cs             BackgroundService: runs `yt-dlp -U` nightly + on demand
  Services/LoggingService.cs            append-only JSONL log at config/logs/logs.jsonl
  Services/ClientInfoService.cs         client IP from X-Forwarded-For / X-Real-IP / CF-Connecting-IP
  Components/Pages/Home.razor           download form, live progress, result player
  Components/Pages/History.razor        list/play/delete files in config/downloads
  Components/Pages/Logs.razor           log viewer with filters
  Components/MediaPlayerModal.razor     <video>/<audio> preview modal
  Components/AboutModal.razor           about/changelog modal
  Components/Layout/*                   sidebar layout, nav, theme toggle
  wwwroot/theme.js                      dark/light theme persistence (localStorage key yt_dlp_theme)
  wwwroot/app.css                       global styles (CSS variables, dark/light themes)
  ServiceWorkerFiles/                   manifest + service worker copied into wwwroot by the Dockerfile (not registered)
  Dockerfile, docker-entrypoint.sh      Alpine image; downloads yt-dlp to /usr/local/bin; drops to uid 1000 via su-exec
  web.config, installNotes.txt          legacy IIS hosting
```

## HTTP surface (Program.cs)

| Method | Path | Purpose |
| --- | --- | --- |
| POST | `/api/download` | JSON `DownloadRequest` -> runs yt-dlp synchronously, returns file URLs |
| GET | `/api/downloads` | list files in `config/downloads` |
| DELETE | `/api/downloads/{fileName}` | delete one file |
| DELETE | `/api/downloads` | delete every file |
| POST | `/api/update` | run `yt-dlp -U` |
| GET | `/download/{fileName}` | file with `Content-Disposition: attachment`, range enabled |
| GET | `/downloads/*` | static file serving of `config/downloads` (inline playback) |

Pages call these endpoints through a server-side `HttpClient` that loops back to the app
(`http://localhost:8080/` when `DOTNET_RUNNING_IN_CONTAINER=true`, else `NavigationManager.BaseUri`).
`Home.razor` calls `IDownloadService` directly for downloads (so it gets progress callbacks) but
uses the HTTP loopback for updates.

## Build and run

```bash
# build
dotnet build yt-dlp_web/yt-dlp_web/yt-dlp_web.csproj

# run locally (needs a yt-dlp binary at /usr/local/bin/yt-dlp, plus ffmpeg on PATH)
dotnet run --project yt-dlp_web/yt-dlp_web/yt-dlp_web.csproj

# container (recommended; this is the supported deployment as of v1.2)
docker compose up --build -d        # http://localhost:7022
```

- The yt-dlp path is hard-coded to `/usr/local/bin/yt-dlp` in both `DownloadService` and
  `UpdateService`. Running natively on Windows/IIS does not work as of v1.2 without changing that.
- `YtDlp:DenoPath` in `appsettings.json` is a Windows path left over from the IIS deployment; it is
  ignored in the container because the file does not exist there (Node.js is installed instead).
- Kestrel listens on `http://0.0.0.0:8080` (appsettings + `ASPNETCORE_URLS`). HTTPS/HSTS are
  expected to be terminated by a reverse proxy.
- There is no test project. Verify changes by building and by running the container and
  exercising Home, History, and Logs in a browser.

## Conventions

- C# with nullable reference types and implicit usings enabled; file-scoped namespaces
  (`namespace yt_dlp_web.Services;`).
- Services are interface + implementation pairs registered in `Program.cs`
  (`IDownloadService` scoped, `ILoggingService` singleton, `IUpdateService` singleton + hosted service).
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
  copying the check.
- Do not commit anything under `config/` (Data Protection keys, logs, downloads). The repo currently
  contains a committed key in `config/keys/`; treat it as compromised.
- Do not add new loopback `HttpClient` calls from server components; inject the service instead.
- Keep file-type lists (audio/video/image/subtitle extensions) in one shared place; several copies
  exist today and have drifted.
- Do not commit build outputs or the `Package/*.zip` artifacts when changing code.

## Known issues (see code review)

- Unauthenticated argument injection into yt-dlp via the URL/format fields (RCE via `--exec`).
- Output files are located by a per-second timestamp, so concurrent downloads can return each other's files.
- No cancellation/timeout for yt-dlp; a live-stream URL downloads forever.
- `caddy/Caddyfile` proxies to port 80 and only `/api` and `/download`, which does not match the app.
- The service worker and web manifest are copied into the image but never registered; manifest icons do not exist.
