# Multi-stage Dockerfile for yt-dlp_web backend
# ─── Build stage ─────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
WORKDIR /src

# Copy project files first so NuGet restore is cached independently of source changes
COPY yt-dlp_web/yt-dlp_web.Client/yt-dlp_web.Client.csproj yt-dlp_web/yt-dlp_web.Client/
COPY yt-dlp_web/yt-dlp_web/yt-dlp_web.csproj yt-dlp_web/yt-dlp_web/
RUN dotnet restore "yt-dlp_web/yt-dlp_web/yt-dlp_web.csproj"

# Copy remaining source and build
COPY . .

# Publish the Blazor WASM client
RUN dotnet publish "yt-dlp_web/yt-dlp_web.Client/yt-dlp_web.Client.csproj" \
	-c Release -o /app/clientpublish

# Publish the backend (trimming disabled to avoid missing STJ source-gen metadata)
RUN dotnet publish "yt-dlp_web/yt-dlp_web/yt-dlp_web.csproj" \
	-c Release -o /app/publish \
	/p:PublishTrimmed=false \
	/p:PublishReadyToRun=false

# ─── Runtime stage ───────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS runtime
ARG YTDLP_URL=https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp
WORKDIR /app

# Install runtime dependencies:
#   curl         – needed by the entrypoint update script
#   ffmpeg       – required by yt-dlp for merging/post-processing
#   nodejs       – provides the JS runtime that yt-dlp needs for YouTube
#   icu-libs     – required by the .NET Globalization stack on Alpine
#   libstdc++    – required by some .NET native components on Alpine
RUN apk add --no-cache curl ffmpeg nodejs python3 icu-libs libstdc++ su-exec tzdata \
	&& curl -L -o /usr/local/bin/yt-dlp "${YTDLP_URL}" \
	&& chmod +x /usr/local/bin/yt-dlp

# Tell the .NET runtime to use the system ICU instead of the invariant mode
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
ENV ASPNETCORE_URLS=http://0.0.0.0:8080

# Copy published application output
COPY --from=build /app/publish .

# Copy Blazor WASM client static assets into wwwroot
COPY --from=build /app/clientpublish/wwwroot/. /app/wwwroot/

# Copy entrypoint, fix line endings, set permissions — all in one layer
COPY yt-dlp_web/yt-dlp_web/docker-entrypoint.sh /app/docker-entrypoint.sh
RUN sed -i 's/\r$//' /app/docker-entrypoint.sh \
	&& addgroup -g 1000 -S appgroup \
	&& adduser -u 1000 -S appuser -G appgroup \
	&& mkdir -p /app/config/keys /app/config/logs /app/config/downloads \
	&& chown -R appuser:appgroup /app /usr/local/bin/yt-dlp \
	&& chmod +x /app/docker-entrypoint.sh

# Container starts as root so the entrypoint can fix volume-mounted dir permissions,
# then drops to appuser via su-exec before launching the app.

EXPOSE 8080
ENTRYPOINT ["/app/docker-entrypoint.sh"]
