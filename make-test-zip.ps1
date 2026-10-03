# Create a test ZIP containing the minimal files needed to run locally with Docker Compose
# Usage (PowerShell):
#  .\make-test-zip.ps1 -OutFile .\yt-dlp_web-test.zip
param(
	[string]$OutFile = "./yt-dlp_web-test.zip"
)

$files = @(
	'docker-compose.yml',
	'.dockerignore',
	'yt-dlp_web/yt-dlp_web/Dockerfile',
	'yt-dlp_web/yt-dlp_web/docker-entrypoint.sh',
	'yt-dlp_web/yt-dlp_web/ServiceWorkerFiles/manifest.webmanifest',
	'yt-dlp_web/yt-dlp_web/ServiceWorkerFiles/service-worker.js',
	'caddy/Caddyfile',
	'docs/docker-and-pwa-setup.md'
)

$found = @()
foreach ($f in $files) {
	if (Test-Path $f) { $found += $f } else { Write-Host "Skipping missing: $f" -ForegroundColor Yellow }
}

if ($found.Count -eq 0) {
	Write-Error "No files found to add to ZIP. Ensure you run this from the repo root.";
	exit 1
}

if (Test-Path $OutFile) { Remove-Item $OutFile -Force }

Write-Host "Creating zip '$OutFile' with the following files:" -ForegroundColor Cyan
$found | ForEach-Object { Write-Host "  - $_" }

Compress-Archive -Path $found -DestinationPath $OutFile -Force
Write-Host "Created $OutFile" -ForegroundColor Green
