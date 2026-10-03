Overview

This repo now includes a compact backend Dockerfile and a docker-compose file that runs Caddy as a reverse proxy and static file server for the Blazor WebAssembly client.

Publish client

1. Build/publish the WebAssembly client static site into ./publish_client

   pwsh> dotnet publish ./yt-dlp_web.Client/yt-dlp_web.Client.csproj -c Release -o ./publish_client

2. Confirm publish_client contains index.html, _framework, and other static files.

Build and run containers

1. From repo root:

   pwsh> docker compose up --build -d

2. The `caddy` service serves files from ./publish_client and proxies /api and /download to the `backend` service.

Notes and tips

- HTTPS: For production, replace the Caddyfile placeholder with your domain in caddy/Caddyfile and Caddy will automatically provision TLS certificates via Let's Encrypt.
- Forwarded headers: When running behind Caddy, your app may receive X-Forwarded-For. Ensure the app's UseForwardedHeaders configuration trusts the proxy or allows forwarded headers from the container network. Current code uses a conservative KnownNetworks set; you can remove KnownNetworks or add the Caddy container address when running in Docker.
- Updating yt-dlp: The Dockerfile downloads the latest yt-dlp during build. Rebuilding the image updates the binary. If you prefer runtime updates, add a startup script to curl the latest yt-dlp on container start.
- ffmpeg: Optional. Uncomment the ffmpeg apt install in Dockerfile if you need audio extraction/format conversion. Note: this increases image size.

Security

- Run containers behind a reverse proxy (Caddy) to get TLS in front of the services.
- The Dockerfile creates a non-root user for runtime; that reduces risk inside the container.

Troubleshooting

- If the backend logs show the server IP instead of the client IP, ensure ForwardedHeaders middleware trusts the proxy. You can trust all forwarded headers in container networks by clearing KnownNetworks/KnownProxies or adding the proxy container's IP.

