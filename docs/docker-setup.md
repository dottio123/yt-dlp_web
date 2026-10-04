# Docker Setup Guide

This guide covers deploying `yt-dlp_web` using Docker Compose and configuring reverse proxies, environment settings, data persistence, and localization.

---

## Quick Start

1. Start the container:
   ```bash
   docker compose up --build -d
   ```
2. Open your browser and navigate to:
   ```text
   http://localhost:7022
   ```

---

## Configuration Settings

The container is configured via environment variables in `docker-compose.yml` or your `.env` file:

| Setting | Default | Description |
| --- | --- | --- |
| `MAX_CONCURRENT_DOWNLOADS` | `4` | Maximum number of concurrent yt-dlp download processes. Extra requests queue and wait. Set to `0` for unlimited concurrent downloads. |
| `DOWNLOAD_TIMEOUT_MINUTES` | `180` | Maximum duration in minutes before an active download process tree is killed. Set to `0` or negative for unlimited. |
| `DISCONNECT_GRACE_SECONDS` | `30` | Grace period in seconds before an active download is aborted when a browser tab disconnects. Set to `0` to cancel immediately. |
| `TZ` | `UTC` | Time zone used for application log timestamps and the nightly yt-dlp update schedule (which runs at local midnight). |
| `YtDlp__ExecutablePath` | *(auto)* | Absolute path to the `yt-dlp` executable. Defaults to `/usr/local/bin/yt-dlp` in the Linux container and `tools/yt-dlp.exe` on Windows. |
| `YtDlp__DenoPath` | *(empty)* | Path to the Deno binary or directory containing `deno`. When provided, prepended to `PATH` for yt-dlp JavaScript extraction. |
| `UPDATE_YTDLP` | `1` | Controls whether `docker-entrypoint.sh` updates yt-dlp on startup. When `1`, downloads the latest binary from `YTDLP_URL` before launching the app. |
| `YTDLP_URL` | `https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp` | URL used by `docker-entrypoint.sh` to download or update the `yt-dlp` binary on container startup. |

---

## Persistent Data (`./config`)

All persistent application data lives in the host directory `./config`, which is mounted into `/app/config` inside the container:

- **`config/keys/`**: Stores ASP.NET Core Data Protection keys used for antiforgery token encryption and cookie protection. **Must never be committed to source control.** To rotate keys, delete the files in this directory.
- **`config/logs/`**: Contains `logs.jsonl`. When `logs.jsonl` reaches 5 MB, it automatically rotates to `logs.1.jsonl`, keeping the previous log file.
- **`config/downloads/`**: Storage location for all downloaded media files, converted audio, extracted subtitles, and thumbnails.

---

## Reverse Proxy Setup

> [!WARNING]
> **No Authentication**: `yt-dlp_web` has no built-in authentication or access control. Anyone who can reach the web interface can trigger downloads and read existing files. Protect the application using reverse proxy authentication (e.g., HTTP Basic Auth) or keep it restricted to a private network / VPN.
>
> **Forwarded Headers**: The application trusts `X-Forwarded-For` from any upstream proxy by default (`KnownNetworks.Clear()`). If the container is exposed directly to the public internet without a trusted reverse proxy, clients can spoof the client IP shown in logs.

### Nginx (Recommended)

When using Nginx to terminate TLS and forward requests to `yt-dlp_web`:

```nginx
location / {
    proxy_pass http://<host>:7022;
    proxy_http_version 1.1;
    proxy_set_header Upgrade $http_upgrade;
    proxy_set_header Connection "upgrade";
    proxy_set_header Host $host;
    proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
    proxy_set_header X-Forwarded-Proto $scheme;
    proxy_read_timeout 1h;
    proxy_buffering off;
}
```

**Directives Explained**:
- `Upgrade` and `Connection "upgrade"`: Required for Blazor Server's WebSocket connection at `/_blazor`.
- `proxy_read_timeout 1h`: Keeps long-lived Blazor WebSocket connections open without premature proxy disconnects.
- `X-Forwarded-For`: Passes the real client IP address to the application for accurate logging.
- `proxy_buffering off`: Disables response buffering so large file downloads stream directly to the client without eating proxy memory or disk cache.

### Caddy (Optional)

If using Caddy as a reverse proxy, the included `caddy/Caddyfile` proxies all traffic including WebSockets automatically:

```caddyfile
# Replace :80 with your domain name (e.g. ytdlp.example.com) for automatic HTTPS
:80 {
	encode gzip
	reverse_proxy yt-dlp-web:8080
}
```

---

## Localization & Culture Note

The Alpine runtime image ships with `icu-libs` and `icu-data-en` (English-only ICU data). Setting `LANG` or `LC_ALL` alone (e.g. `LANG=de_DE.UTF-8`) will set the culture name but falls back to English/invariant number and date formats. Real locale-specific formatting (such as German decimal commas) requires installing `icu-data-full` in the Alpine container.
