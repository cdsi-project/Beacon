using System.Text.Json;

namespace CDSI.Agent.WinForms;

internal static class ShellFileImportRequest
{
    internal const int MaximumPayloadBytes = 64 * 1024;
    internal const int MaximumFiles = 100;

    public static string[] ParseArguments(IReadOnlyList<string> arguments)
    {
        if (arguments.Count == 0 || arguments[0] != "--import-file")
        {
            return [];
        }
        return NormalizePaths(arguments.Skip(1).ToArray());
    }

    public static string[] NormalizePaths(IReadOnlyCollection<string> paths)
    {
        if (paths.Count is < 1 or > MaximumFiles)
        {
            throw new ArgumentException("右键登记请求必须包含 1 至 100 个文件。");
        }
        var normalized = paths.Select(path =>
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            {
                throw new ArgumentException("右键登记请求只接受完整文件路径。");
            }
            return Path.GetFullPath(path);
        }).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (JsonSerializer.SerializeToUtf8Bytes(normalized).Length > MaximumPayloadBytes)
        {
            throw new ArgumentException("右键登记请求过大，请分批选择文件。");
        }
        return normalized;
    }
}
