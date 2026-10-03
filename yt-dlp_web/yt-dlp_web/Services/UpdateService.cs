namespace yt_dlp_web.Services;

public class UpdateResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public bool AlreadyLatest { get; set; }
}

public interface IUpdateService
{
    Task<UpdateResult> RunUpdate();
}

public class UpdateService : BackgroundService, IUpdateService
{
    private readonly ILoggingService _logger;
    private readonly ILogger<UpdateService> _log;

    public UpdateService(ILoggingService logger, ILogger<UpdateService> log)
    {
        _logger = logger;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Calculate time until next midnight
                var now = DateTime.Now;
                var nextMidnight = now.Date.AddDays(1);
                var delay = nextMidnight - now;

                _log.LogInformation($"Next yt-dlp update scheduled in {delay.TotalHours:F1} hours");

                // Wait until midnight
                await Task.Delay(delay, stoppingToken);

                // Run the update
                await RunUpdate();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Unexpected error in update service: {ex.Message}");
                _log.LogError(ex, "Error in update service");

                // Wait 1 minute before retrying
                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    public async Task<UpdateResult> RunUpdate()
    {
        try
        {
            var ytDlpPath = "/usr/local/bin/yt-dlp";

            if (!File.Exists(ytDlpPath))
            {
                _logger.LogUpdate($"yt-dlp not found at {ytDlpPath}", false);
                return new UpdateResult { Success = false, Message = $"yt-dlp not found at {ytDlpPath}" };
            }

            _log.LogInformation("Starting yt-dlp update...");

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = ytDlpPath,
                Arguments = "-U",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc is null)
            {
                _logger.LogUpdate("Failed to start update process", false);
                return new UpdateResult { Success = false, Message = "Failed to start update process" };
            }

            var stdOut = await proc.StandardOutput.ReadToEndAsync();
            var stdErr = await proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync();

            if (proc.ExitCode == 0)
            {
                var output = stdOut + stdErr;
                
                // Check for "is up to date" message
                bool isAlreadyLatest = output.Contains("is up to date", StringComparison.OrdinalIgnoreCase);
                
                string message;
                if (isAlreadyLatest)
                {
                    // Extract version from "yt-dlp is up to date (stable@2026.03.17 from yt-dlp/yt-dlp)"
                    var versionMatch = System.Text.RegularExpressions.Regex.Match(output, @"up to date\s*\(([^)]+)\)");
                    var version = versionMatch.Success ? versionMatch.Groups[1].Value : "current version";
                    message = $"yt-dlp is up to date ({version})";
                }
                else
                {
                    // Check if it actually updated
                    if (output.Contains("Updated yt-dlp to", StringComparison.OrdinalIgnoreCase))
                    {
                        // Extract version from "Updated yt-dlp to stable@2026.03.17 from yt-dlp/yt-dlp"
                        var versionMatch = System.Text.RegularExpressions.Regex.Match(output, @"Updated yt-dlp to\s+([^\s]+)");
                        var version = versionMatch.Success ? versionMatch.Groups[1].Value : "latest";
                        message = $"yt-dlp updated successfully to {version}";
                    }
                    else
                    {
                        message = $"yt-dlp update completed at {DateTime.Now:g}";
                    }
                }

                _logger.LogUpdate(message, true);
                _log.LogInformation("yt-dlp update completed successfully");
                
                return new UpdateResult 
                { 
                    Success = true, 
                    Message = message,
                    AlreadyLatest = isAlreadyLatest
                };
            }
            else
            {
                var msg = $"Update failed with exit code {proc.ExitCode}. {stdErr}";
                _logger.LogUpdate(msg, false);
                _log.LogWarning($"yt-dlp update failed: {msg}");
                
                return new UpdateResult { Success = false, Message = msg };
            }
        }
        catch (Exception ex)
        {
            _logger.LogUpdate($"Update error: {ex.Message}", false);
            _log.LogError(ex, "Error running yt-dlp update");
            
            return new UpdateResult { Success = false, Message = $"Update error: {ex.Message}" };
        }
    }
}
