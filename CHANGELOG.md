# Changelog

## v1.2.1 — 2026-10-05

Security, reliability and subtitle fixes from a full code review.

### Security
- Download options are passed to yt-dlp as separate arguments and checked against allow-lists, so a crafted URL or format can no longer inject extra yt-dlp options (such as `--exec`).
- File names for download and delete are strictly resolved inside the downloads folder; path tricks like `../` are rejected.
- `config/` (Data Protection keys, logs, downloads) is no longer tracked in git. Delete `config/keys/*.xml` on existing installs to rotate the key.
- `/api/download` no longer accepts a client IP from the request body.

### Downloads
- Simultaneous downloads always get their own files (each job has a unique token in its file name).
- New **Cancel** button. Closing the browser tab cancels its download after a short grace period.
- Live streams are rejected with a clear message instead of downloading forever.
- Configurable timeout and concurrent-download limit (`DOWNLOAD_TIMEOUT_MINUTES`, `MAX_CONCURRENT_DOWNLOADS`, `DISCONNECT_GRACE_SECONDS`).
- Very long titles are shortened so downloads don't fail with "file name too long".
- yt-dlp now uses Node.js as its JavaScript runtime for YouTube, so all formats are available.

### Subtitles
- "English Only" now matches regional tracks (`en-US`, `en-GB`, `en-AU`…), not just `en`.
- The History player now shows subtitles, and both players offer every WebVTT track.
- Subtitle tracks carry their real language code instead of always `en`.

### Fixes and polish
- Progress shows the estimated total size when the exact size is unknown.
- Progress bar works in locales that use a decimal comma.
- Update errors appear in their own card instead of "Download Failed"; only one update runs at a time.
- One damaged log line no longer hides all logs; logs rotate at 5 MB.
- The yt-dlp path is configurable (`YtDlp__ExecutablePath`), and a missing binary shows a clear error.
- `TZ` setting for log timestamps and the nightly update.
- Pages call services directly instead of looping back over HTTP.

### Docs and tooling
- New `docs/docker-setup.md` with Nginx and Caddy reverse-proxy examples.
- `AGENTS.md` guide for AI coding agents.
- xUnit test project (70 tests).
- Removed unused PWA files; fixed the example Caddyfile.

## v1.2.0

Real-time progress and media preview player.
- Live progress bar with percentage, speed, ETA and size.
- Smoothed speed and ETA updates.
- In-app player on Home and in History.
- Dark and light themes with YouTube-red accents.
- Refreshed responsive layout and icons.

## v1.1.0

History and diagnostics.
- Download history with size and date sorting.
- Filterable logs for downloads, updates and errors.

## v1.0.0

Initial release.
- Containerized web interface for yt-dlp.
- Automatic daily yt-dlp updates and a manual update button.
