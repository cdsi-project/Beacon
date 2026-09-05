using System.Text;

namespace CDSI.Agent.WinForms;

public sealed class RuntimeLogService
{
    private readonly object _writeLock = new();
    private string _logDirectory;
    private string _currentLogPath;

    public RuntimeLogService(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        _logDirectory = Path.Combine(Path.GetFullPath(dataDirectory), "Logs");
        _currentLogPath = Path.Combine(
            _logDirectory,
            $"runtime-{DateTimeOffset.Now:yyyyMMdd-HHmmss-fff}.log");

        WriteInformation(
            $"CDSI Beacon v{MainForm.GetApplicationVersion()} 启动；" +
            $"OS={Environment.OSVersion}；Runtime={Environment.Version}");
    }

    public string LogDirectory
    {
        get
        {
            lock (_writeLock)
            {
                return _logDirectory;
            }
        }
    }

    public string CurrentLogPath
    {
        get
        {
            lock (_writeLock)
            {
                return _currentLogPath;
            }
        }
    }

    public bool TryUseWorkspace(string workspacePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);
        try
        {
            var targetDirectory = GetWorkspaceLogDirectory(workspacePath);
            lock (_writeLock)
            {
                if (PathsEqual(_logDirectory, targetDirectory))
                {
                    return true;
                }

                Directory.CreateDirectory(targetDirectory);
                var targetLogPath = CreateAvailableLogPath(
                    targetDirectory,
                    Path.GetFileName(_currentLogPath));
                if (File.Exists(_currentLogPath))
                {
                    File.Copy(_currentLogPath, targetLogPath, overwrite: false);
                }

                _logDirectory = targetDirectory;
                _currentLogPath = targetLogPath;
            }

            WriteInformation($"运行日志目录已切换到工作目录：{targetDirectory}");
            return true;
        }
        catch (Exception exception)
        {
            WriteError("无法将运行日志目录切换到工作目录", exception);
            return false;
        }
    }

    public static string GetWorkspaceLogDirectory(string workspacePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);
        return Path.Combine(
            Path.GetFullPath(workspacePath),
            "System",
            "Logs");
    }

    public void WriteInformation(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        WriteEntry("INFO", message);
    }

    public void WriteError(string context, Exception exception)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(context);
        ArgumentNullException.ThrowIfNull(exception);
        WriteEntry("ERROR", $"{context}{Environment.NewLine}{exception}");
    }

    public IReadOnlyList<string> GetLogFiles()
    {
        var logDirectory = LogDirectory;
        try
        {
            Directory.CreateDirectory(logDirectory);
            return Directory
                .EnumerateFiles(logDirectory, "*.log", SearchOption.TopDirectoryOnly)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .ThenByDescending(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    public string ReadLogFile(string logPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logPath);
        var logDirectory = LogDirectory;
        var fullPath = Path.GetFullPath(logPath);
        var relativePath = Path.GetRelativePath(logDirectory, fullPath);
        if (Path.IsPathRooted(relativePath) ||
            relativePath.Equals("..", StringComparison.Ordinal) ||
            relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("只能读取 Beacon 日志目录中的文件。");
        }

        using var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private void WriteEntry(string level, string message)
    {
        try
        {
            var redacted = StartupFailureReporter.RedactSensitiveText(message);
            var entry = $"{DateTimeOffset.Now:O} [{level}] {redacted}{Environment.NewLine}";
            lock (_writeLock)
            {
                Directory.CreateDirectory(_logDirectory);
                File.AppendAllText(_currentLogPath, entry, Encoding.UTF8);
            }
        }
        catch
        {
            // Logging must never prevent Beacon from starting or completing an operation.
        }
    }

    private static string CreateAvailableLogPath(
        string logDirectory,
        string filename)
    {
        var candidate = Path.Combine(logDirectory, filename);
        if (!File.Exists(candidate))
        {
            return candidate;
        }

        var name = Path.GetFileNameWithoutExtension(filename);
        var extension = Path.GetExtension(filename);
        for (var suffix = 2; ; suffix++)
        {
            candidate = Path.Combine(logDirectory, $"{name}-{suffix}{extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);
    }
}
