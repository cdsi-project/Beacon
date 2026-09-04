namespace CDSI.Agent.WinForms.Tests;

public sealed class StartupFailureReporterTests
{
    [Fact]
    public void CreateUserFacingMessage_IncludesStateRestoreSafetyBackupPath()
    {
        var safetyPath = Path.Combine(Path.GetTempPath(), "Beacon Safety", "restore-3");
        var exception = new CDSI.Agent.Infrastructure.Persistence.StateRestoreFailedException(
            "状态恢复和回滚均失败。",
            currentStateIsSafe: false,
            new IOException("failure"),
            safetyPath);

        var message = StartupFailureReporter.CreateUserFacingMessage(exception);

        Assert.Contains("状态恢复和回滚均失败", message);
        Assert.Contains(Path.GetFullPath(safetyPath), message);
    }

    [Fact]
    public void CreateUserFacingMessage_DoesNotMaskFailureWhenSafetyPathIsInvalid()
    {
        var exception = new CDSI.Agent.Infrastructure.Persistence.StateRestoreFailedException(
            "原始启动错误",
            currentStateIsSafe: false,
            new IOException("failure"),
            "invalid\0path");

        var message = StartupFailureReporter.CreateUserFacingMessage(exception);

        Assert.Contains("原始启动错误", message);
        Assert.DoesNotContain("invalid", message);
        Assert.Null(StartupFailureReporter.NormalizeReportablePath("invalid\0path"));
    }

    [Fact]
    public void RedactSensitiveText_RemovesCredentialsAndUrlQueries()
    {
        const string input =
            "AccessKeySecret=very-secret password: hidden " +
            "https://example.com/object?signature=abc&token=def";

        var redacted = StartupFailureReporter.RedactSensitiveText(input);

        Assert.DoesNotContain("very-secret", redacted);
        Assert.DoesNotContain("hidden", redacted);
        Assert.DoesNotContain("signature=abc", redacted);
        Assert.Contains("AccessKeySecret=[REDACTED]", redacted);
        Assert.Contains("https://example.com/object?[REDACTED]", redacted);
    }

    [Fact]
    public void TryWriteLog_WritesOnlyInsideTheProvidedDataDirectory()
    {
        var testRoot = Path.Combine(
            Path.GetTempPath(),
            "cdsi-agent-tests",
            Guid.NewGuid().ToString("N"));
        try
        {
            var logPath = StartupFailureReporter.TryWriteLog(
                testRoot,
                new InvalidOperationException("startup failed"));

            Assert.NotNull(logPath);
            Assert.True(File.Exists(logPath));
            Assert.StartsWith(
                Path.GetFullPath(testRoot),
                Path.GetFullPath(logPath),
                StringComparison.OrdinalIgnoreCase);
            Assert.Contains(
                "InvalidOperationException",
                File.ReadAllText(logPath));
        }
        finally
        {
            if (Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, recursive: true);
            }
        }
    }
}
