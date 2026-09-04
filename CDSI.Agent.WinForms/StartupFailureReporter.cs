using System.Text;
using System.Text.RegularExpressions;
using CDSI.Agent.Infrastructure.Persistence;

namespace CDSI.Agent.WinForms;

internal static class StartupFailureReporter
{
    private static readonly Regex SecretAssignmentPattern = new(
        @"(?i)\b(accesskeysecret|secret|password|token|signature)\b(\s*[:=]\s*)([^\s,;]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex UrlQueryPattern = new(
        @"(https?://[^\s?]+)\?[^\s]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string? TryWriteLog(string dataDirectory, Exception exception)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        ArgumentNullException.ThrowIfNull(exception);
        try
        {
            var logDirectory = Path.Combine(dataDirectory, "Logs");
            Directory.CreateDirectory(logDirectory);
            var logPath = Path.Combine(
                logDirectory,
                $"startup-error-{DateTimeOffset.Now:yyyyMMdd-HHmmss-fff}.log");
            var content = new StringBuilder()
                .AppendLine($"Timestamp: {DateTimeOffset.Now:O}")
                .AppendLine($"Version: {MainForm.GetApplicationVersion()}")
                .AppendLine($"OS: {Environment.OSVersion}")
                .AppendLine($"Runtime: {Environment.Version}")
                .AppendLine()
                .AppendLine(RedactSensitiveText(exception.ToString()))
                .ToString();
            File.WriteAllText(logPath, content, Encoding.UTF8);
            return logPath;
        }
        catch
        {
            return null;
        }
    }

    public static void Show(string dataDirectory, Exception exception)
    {
        var logPath = TryWriteLog(dataDirectory, exception);
        var logText = logPath is null
            ? string.Empty
            : $"\n\n诊断日志：{logPath}";
        MessageBox.Show(
            $"CDSI Beacon 启动失败。\n\n{CreateUserFacingMessage(exception)}{logText}",
            "CDSI Beacon",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }

    internal static string CreateUserFacingMessage(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var reportableSafetyPath = exception is StateRestoreFailedException
        {
            SafetyBackupPath: { } path
        }
            ? NormalizeReportablePath(path)
            : null;
        var safetyBackupText =
            reportableSafetyPath is not null
                ? $"\n\n恢复前安全副本位置：\n{reportableSafetyPath}"
                : string.Empty;
        return RedactSensitiveText($"{exception.Message}{safetyBackupText}");
    }

    internal static string? NormalizeReportablePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception exception) when (exception is ArgumentException or
                                          NotSupportedException or
                                          PathTooLongException or
                                          System.Security.SecurityException)
        {
            return null;
        }
    }

    internal static string RedactSensitiveText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var redacted = SecretAssignmentPattern.Replace(
            text,
            match => $"{match.Groups[1].Value}{match.Groups[2].Value}[REDACTED]");
        return UrlQueryPattern.Replace(redacted, "$1?[REDACTED]");
    }
}
