using System.Text.Json;

namespace yt_dlp_web.Services;

public class LogEntry
{
    public DateTime Timestamp { get; set; }
    public string Type { get; set; } = string.Empty; // "Download", "Update", "Error"
    public string Message { get; set; } = string.Empty;
    public string? ClientIp { get; set; }
    public string? FileName { get; set; }
    public string? RelatedType { get; set; } // "Download", "Update", or null for general errors
}

public interface ILoggingService
{
    void LogDownload(string fileName, string? clientIp);
    void LogUpdate(string message, bool success);
    void LogError(string message, string? clientIp = null, string? relatedType = null);
    List<LogEntry> GetLogs(int limit = 100);
    List<LogEntry> GetDownloadLogs(int limit = 50);
    List<LogEntry> GetUpdateLogs(int limit = 50);
}

public class LoggingService : ILoggingService
{
    private readonly string _logsPath;
    private readonly object _lockObj = new();

    public LoggingService(string logsPath)
    {
        _logsPath = logsPath;
        Directory.CreateDirectory(_logsPath);
    }

    public void LogDownload(string fileName, string? clientIp)
    {
        var entry = new LogEntry
        {
            Timestamp = DateTime.Now,
            Type = "Download",
            Message = $"Downloaded: {fileName}",
            ClientIp = clientIp,
            FileName = fileName
        };
        SaveEntry(entry);
    }

    public void LogUpdate(string message, bool success)
    {
        var entry = new LogEntry
        {
            Timestamp = DateTime.Now,
            Type = "Update",
            Message = success ? $"✓ Update successful: {message}" : $"✗ Update failed: {message}"
        };
        SaveEntry(entry);
    }

    public void LogError(string message, string? clientIp = null, string? relatedType = null)
    {
        var entry = new LogEntry
        {
            Timestamp = DateTime.Now,
            Type = "Error",
            Message = message,
            ClientIp = clientIp,
            RelatedType = relatedType
        };
        SaveEntry(entry);
    }

    public List<LogEntry> GetLogs(int limit = 100)
    {
        lock (_lockObj)
        {
            return ReadLogs().TakeLast(limit).ToList();
        }
    }

    public List<LogEntry> GetDownloadLogs(int limit = 50)
    {
        lock (_lockObj)
        {
            return ReadLogs()
                .Where(l => l.Type == "Download" || (l.Type == "Error" && l.RelatedType == "Download"))
                .TakeLast(limit)
                .ToList();
        }
    }

    public List<LogEntry> GetUpdateLogs(int limit = 50)
    {
        lock (_lockObj)
        {
            return ReadLogs()
                .Where(l => l.Type == "Update" || (l.Type == "Error" && l.RelatedType == "Update"))
                .TakeLast(limit)
                .ToList();
        }
    }

    private void SaveEntry(LogEntry entry)
    {
        lock (_lockObj)
        {
            var logsFile = Path.Combine(_logsPath, "logs.jsonl");
            var json = JsonSerializer.Serialize(entry);
            File.AppendAllText(logsFile, json + Environment.NewLine);
        }
    }

    private List<LogEntry> ReadLogs()
    {
        var logsFile = Path.Combine(_logsPath, "logs.jsonl");
        if (!File.Exists(logsFile))
            return new();

        try
        {
            return File.ReadAllLines(logsFile)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(line => JsonSerializer.Deserialize<LogEntry>(line))
                .Where(e => e != null)
                .Cast<LogEntry>()
                .ToList();
        }
        catch
        {
            return new();
        }
    }
}
