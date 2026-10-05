namespace yt_dlp_web.Services;

public sealed record DownloadFileInfo(string Name, long Size, DateTime Modified, string DownloadUrl);

public interface IDownloadStore
{
    string RootPath { get; }
    bool TryResolve(string? fileName, out string fullPath);
    IReadOnlyList<DownloadFileInfo> List();
    bool Delete(string fileName);
    int DeleteAll();
}

public sealed class DownloadStore : IDownloadStore
{
    public string RootPath { get; }

    public DownloadStore(string rootPath)
    {
        var full = Path.GetFullPath(rootPath);
        if (full.Length > 1 && (full.EndsWith(Path.DirectorySeparatorChar) || full.EndsWith(Path.AltDirectorySeparatorChar)))
        {
            full = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        RootPath = full;
        Directory.CreateDirectory(RootPath);
    }

    public bool TryResolve(string? fileName, out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(fileName))
            return false;

        if (fileName == "." || fileName == "..")
            return false;

        try
        {
            if (Path.GetFileName(fileName) != fileName)
                return false;

            var full = Path.GetFullPath(Path.Combine(RootPath, fileName));
            var dir = Path.GetDirectoryName(full);
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!string.Equals(dir, RootPath, comparison))
                return false;

            fullPath = full;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public IReadOnlyList<DownloadFileInfo> List()
    {
        if (!Directory.Exists(RootPath))
            return Array.Empty<DownloadFileInfo>();

        var directoryInfo = new DirectoryInfo(RootPath);
        return directoryInfo.EnumerateFiles()
            .OrderByDescending(f => f.LastWriteTime)
            .Select(f => new DownloadFileInfo(
                f.Name,
                f.Length,
                f.LastWriteTime,
                $"download/{Uri.EscapeDataString(f.Name)}"))
            .ToList();
    }

    public bool Delete(string fileName)
    {
        if (!TryResolve(fileName, out var fullPath))
            return false;

        try
        {
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    public int DeleteAll()
    {
        if (!Directory.Exists(RootPath))
            return 0;

        var count = 0;
        foreach (var file in Directory.GetFiles(RootPath))
        {
            try
            {
                if (File.Exists(file))
                {
                    File.Delete(file);
                    count++;
                }
            }
            catch
            {
                // Continue deleting remaining files
            }
        }

        return count;
    }
}
