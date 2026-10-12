using CDSI.Agent.Core.Abstractions;

namespace CDSI.Agent.Application.Assets;

public sealed class SelectedFileImportService(
    IAssetRepository repository,
    ILocalFileInspector inspector)
{
    public async Task<SelectedFileImportResult> ImportAsync(
        IReadOnlyCollection<string> paths,
        IReadOnlyCollection<string> protectedDirectories,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(protectedDirectories);
        if (paths.Count is < 1 or > 1_000)
        {
            throw new ArgumentException("一次最多登记 1000 个文件。", nameof(paths));
        }

        var deviceId = await repository.GetOrCreateDeviceIdAsync(cancellationToken);
        var seen = new HashSet<string>(OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var items = new List<SelectedFileImportItem>();
        foreach (var path in paths)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return new SelectedFileImportResult(items, Cancelled: true);
            }
            try
            {
                if (!Path.IsPathFullyQualified(path))
                {
                    throw new ArgumentException("文件路径必须是绝对路径。");
                }
                var fullPath = Path.GetFullPath(path);
                if (!seen.Add(fullPath))
                {
                    continue;
                }
                progress?.Report(fullPath);
                if (protectedDirectories.Any(directory => IsWithin(fullPath, directory)))
                {
                    throw new IOException("Beacon 系统数据目录中的文件不能加入资产列表。");
                }
                var file = inspector.Inspect(fullPath);
                var registered = await repository.RegisterLocalFilesAsync(
                    deviceId, [file], DateTimeOffset.UtcNow, cancellationToken,
                    restoreHiddenAssets: true);
                items.Add(new SelectedFileImportItem(fullPath, registered.Single().AssetId, null));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return new SelectedFileImportResult(items, Cancelled: true);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                items.Add(new SelectedFileImportItem(path, null, exception.Message));
            }
        }
        return new SelectedFileImportResult(items, Cancelled: false);
    }

    private static bool IsWithin(string path, string directory)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return path.Equals(root, comparison) ||
            path.StartsWith(Path.EndsInDirectorySeparator(root)
                ? root : root + Path.DirectorySeparatorChar, comparison);
    }
}

public sealed record SelectedFileImportItem(string Path, Guid? AssetId, string? Error);

public sealed record SelectedFileImportResult(
    IReadOnlyList<SelectedFileImportItem> Items,
    bool Cancelled)
{
    public int ImportedCount => Items.Count(item => item.AssetId is not null);
    public int FailedCount => Items.Count(item => item.Error is not null);
}
