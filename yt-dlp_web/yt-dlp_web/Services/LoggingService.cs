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
    private readonly long _maxFileBytes;
    private readonly object _lockObj = new();

    public LoggingService(string logsPath, long maxFileBytes = 5 * 1024 * 1024)
    {
        _logsPath = logsPath;
        _maxFileBytes = maxFileBytes;
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
            var current = Path.Combine(_logsPath, "logs.jsonl");
            var previous = Path.Combine(_logsPath, "logs.1.jsonl");

            if (File.Exists(current))
            {
                var fileInfo = new FileInfo(current);
                if (fileInfo.Length >= _maxFileBytes)
                {
                    File.Move(current, previous, overwrite: true);
                }
            }

            var json = JsonSerializer.Serialize(entry);
            File.AppendAllText(current, json + Environment.NewLine);
        }
    }

    private List<LogEntry> ReadLogs()
    {
        var entries = new List<LogEntry>();
        var previous = Path.Combine(_logsPath, "logs.1.jsonl");
        var current = Path.Combine(_logsPath, "logs.jsonl");

        ReadLinesInto(previous, entries);
        ReadLinesInto(current, entries);

        return entries;
    }

    private static void ReadLinesInto(string filePath, List<LogEntry> entries)
    {
        if (!File.Exists(filePath))
        {
            return;
        }

        foreach (var line in File.ReadLines(filePath))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                var entry = JsonSerializer.Deserialize<LogEntry>(line);
                if (entry != null)
                {
                    entries.Add(entry);
                }
            }
            catch (JsonException)
            {
                // Skip malformed lines individually
            }
        }
    }
}
