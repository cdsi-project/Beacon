using CDSI.Agent.Core.Assets;

namespace CDSI.Agent.WinForms;

internal sealed class AssetDirectoryTreeNode(string path)
{
    public string Path { get; } = path;
    public AssetDirectoryTreeNode? Parent { get; set; }
    public List<AssetDirectoryTreeNode> Children { get; } = [];
    public AssetDirectorySummary Summary { get; set; } =
        new(path, 0, 0, 0, 0, DateTimeOffset.MinValue);
    public bool HasDirectAssets { get; set; }
    public bool IsExpanded { get; set; }
}

internal sealed record AssetDirectoryTreeRow(AssetDirectoryTreeNode Node, int Depth);

internal static class AssetDirectoryTree
{
    public static IReadOnlyList<AssetDirectoryTreeNode> Build(
        IReadOnlyList<AssetDirectorySummary> directories)
    {
        var nodes = new Dictionary<string, AssetDirectoryTreeNode>(StringComparer.OrdinalIgnoreCase);
        foreach (var directory in directories)
        {
            var path = System.IO.Path.TrimEndingDirectorySeparator(
                System.IO.Path.GetFullPath(directory.Path));
            var node = GetOrAdd(path);
            node.HasDirectAssets = true;
            node.Summary = Add(node.Summary, directory);
            while (System.IO.Path.GetDirectoryName(path) is { Length: > 0 } parentPath)
            {
                var parent = GetOrAdd(parentPath);
                if (node.Parent is not null)
                {
                    break;
                }
                node.Parent = parent;
                parent.Children.Add(node);
                node = parent;
                path = parentPath;
            }
        }

        // A post-order traversal aggregates each direct directory exactly once.
        var roots = nodes.Values.Where(node => node.Parent is null).ToArray();
        var traversal = new Stack<AssetDirectoryTreeNode>(roots);
        var ordered = new List<AssetDirectoryTreeNode>(nodes.Count);
        while (traversal.TryPop(out var node))
        {
            ordered.Add(node);
            foreach (var child in node.Children)
            {
                traversal.Push(child);
            }
        }
        for (var i = ordered.Count - 1; i >= 0; i--)
        {
            var node = ordered[i];
            if (node.Parent is { } parent)
            {
                parent.Summary = Add(parent.Summary, node.Summary);
            }
        }

        // Omit empty single-child ancestors above the first useful grouping.
        return roots.Select(root =>
        {
            while (!root.HasDirectAssets && root.Children.Count == 1)
            {
                root = root.Children[0];
            }
            root.IsExpanded = true;
            return root;
        }).OrderBy(root => root.Path, StringComparer.OrdinalIgnoreCase).ToArray();

        AssetDirectoryTreeNode GetOrAdd(string path)
        {
            if (!nodes.TryGetValue(path, out var node))
            {
                node = new AssetDirectoryTreeNode(path);
                nodes.Add(path, node);
            }
            return node;
        }
    }

    public static IReadOnlyList<AssetDirectoryTreeRow> GetVisibleRows(
        IReadOnlyList<AssetDirectoryTreeNode> roots,
        int sortColumn = 1,
        bool descending = false)
    {
        var rows = new List<AssetDirectoryTreeRow>();
        var comparer = Comparer<AssetDirectoryTreeNode>.Create((left, right) =>
        {
            var result = sortColumn switch
            {
                2 => left.Summary.AssetCount.CompareTo(right.Summary.AssetCount),
                3 => left.Summary.AvailableAssetCount.CompareTo(right.Summary.AvailableAssetCount),
                4 => left.Summary.MissingAssetCount.CompareTo(right.Summary.MissingAssetCount),
                5 => left.Summary.AvailableSizeBytes.CompareTo(right.Summary.AvailableSizeBytes),
                6 => left.Summary.LatestModifiedAt.CompareTo(right.Summary.LatestModifiedAt),
                _ => StringComparer.OrdinalIgnoreCase.Compare(left.Path, right.Path)
            };
            if (result == 0)
            {
                result = StringComparer.OrdinalIgnoreCase.Compare(left.Path, right.Path);
            }
            return descending ? -result : result;
        });
        var pending = new Stack<AssetDirectoryTreeRow>(roots.OrderByDescending(node => node, comparer)
            .Select(node => new AssetDirectoryTreeRow(node, 0)));
        while (pending.TryPop(out var row))
        {
            rows.Add(row);
            if (row.Node.IsExpanded)
            {
                foreach (var child in row.Node.Children.OrderByDescending(node => node, comparer))
                {
                    pending.Push(new AssetDirectoryTreeRow(child, row.Depth + 1));
                }
            }
        }
        return rows;
    }

    public static IEnumerable<AssetDirectoryTreeNode> AllNodes(
        IReadOnlyList<AssetDirectoryTreeNode> roots)
    {
        var pending = new Stack<AssetDirectoryTreeNode>(roots);
        while (pending.TryPop(out var node))
        {
            yield return node;
            foreach (var child in node.Children)
            {
                pending.Push(child);
            }
        }
    }

    private static AssetDirectorySummary Add(AssetDirectorySummary left, AssetDirectorySummary right) =>
        new(left.Path,
            left.AssetCount + right.AssetCount,
            left.AvailableAssetCount + right.AvailableAssetCount,
            left.MissingAssetCount + right.MissingAssetCount,
            long.MaxValue - left.AvailableSizeBytes < right.AvailableSizeBytes
                ? long.MaxValue
                : left.AvailableSizeBytes + right.AvailableSizeBytes,
            left.LatestModifiedAt > right.LatestModifiedAt ? left.LatestModifiedAt : right.LatestModifiedAt);
}
