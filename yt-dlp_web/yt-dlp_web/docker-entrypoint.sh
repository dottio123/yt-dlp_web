#!/bin/sh
set -eu

# Optional: update yt-dlp at container start if UPDATE_YTDLP is set to '1'
if [ "${UPDATE_YTDLP:-0}" = "1" ]; then
  echo "Updating yt-dlp to latest..."
  if command -v curl > /dev/null 2>&1; then
    curl -L -o /usr/local/bin/yt-dlp "${YTDLP_URL:-https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp}"
    chmod +x /usr/local/bin/yt-dlp
    echo "yt-dlp updated"
  else
    echo "curl not available to update yt-dlp"
  fi
fi

# Fix permissions on the volume-mounted config directory and its subdirectories.
# This runs as root so it can chown even files created by the host or previous containers.
mkdir -p /app/config/keys /app/config/logs /app/config/downloads
chown -R appuser:appgroup /app/config

# Drop privileges and start the application as appuser
exec su-exec appuser dotnet yt-dlp_web.dll "$@"
