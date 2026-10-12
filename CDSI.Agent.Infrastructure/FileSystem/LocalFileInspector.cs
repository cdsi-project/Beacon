using CDSI.Agent.Core.Abstractions;
using CDSI.Agent.Core.Scanning;

namespace CDSI.Agent.Infrastructure.FileSystem;

public sealed class LocalFileInspector : ILocalFileInspector
{
    public DiscoveredFile Inspect(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException("文件路径必须是绝对路径。");
        }

        var file = new FileInfo(Path.GetFullPath(path));
        if (!file.Exists || (file.Attributes & FileAttributes.Directory) != 0)
        {
            throw new FileNotFoundException("文件不存在或不是普通文件。", path);
        }
        if ((file.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException("暂不登记符号链接或重解析点文件。");
        }
        for (var parent = file.Directory; parent is not null; parent = parent.Parent)
        {
            if ((parent.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException("暂不登记符号链接或 junction 目录中的文件。");
            }
        }

        var extension = file.Extension.ToLowerInvariant();
        return new DiscoveredFile(file.FullName, file.Name, extension,
            MimeTypeDetector.Detect(extension), file.Length,
            new DateTimeOffset(file.CreationTimeUtc),
            new DateTimeOffset(file.LastWriteTimeUtc));
    }
}
