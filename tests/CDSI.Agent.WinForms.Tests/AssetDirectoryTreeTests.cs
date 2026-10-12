using CDSI.Agent.Core.Assets;

namespace CDSI.Agent.WinForms.Tests;

public sealed class AssetDirectoryTreeTests
{
    [Fact]
    public void Build_FillsIntermediateParentsAndAggregatesWithoutDoubleCounting()
    {
        var root = @"D:\素材";
        var latest = DateTimeOffset.Parse("2026-10-12T08:00:00Z");
        var roots = AssetDirectoryTree.Build([
            Summary(root, 1, 10),
            new AssetDirectorySummary(root + @"\项目A\视频", 3, 2, 1, 50, latest),
            Summary(root + @"\项目B", 2, 40)
        ]);

        var parent = Assert.Single(roots);
        Assert.Equal(root, parent.Path);
        Assert.Equal(6, parent.Summary.AssetCount);
        Assert.Equal(5, parent.Summary.AvailableAssetCount);
        Assert.Equal(1, parent.Summary.MissingAssetCount);
        Assert.Equal(100, parent.Summary.AvailableSizeBytes);
        Assert.Equal(latest, parent.Summary.LatestModifiedAt);
        Assert.Equal(2, parent.Children.Count);
        var project = parent.Children.Single(node => node.Path.EndsWith("项目A"));
        Assert.False(project.HasDirectAssets);
        Assert.Equal(3, project.Summary.AssetCount);
        Assert.Equal(50, project.Summary.AvailableSizeBytes);
        Assert.Equal(root + @"\项目A\视频", Assert.Single(project.Children).Path);
    }

    [Fact]
    public void Build_GroupsSiblingsWithNoDirectAssetsInTheirSharedParent()
    {
        var root = Assert.Single(AssetDirectoryTree.Build([
            Summary(@"D:\素材\项目A", 1, 10),
            Summary(@"D:\素材\项目B", 1, 20)
        ]));

        Assert.Equal(@"D:\素材", root.Path);
        Assert.Equal(2, root.Summary.AssetCount);
        Assert.Equal(30, root.Summary.AvailableSizeBytes);
        Assert.False(root.HasDirectAssets);
    }

    [Fact]
    public void Build_KeepsDriveAndUncShareBoundariesAndSimilarNamesSeparate()
    {
        var roots = AssetDirectoryTree.Build([
            Summary(@"D:\素材\A", 1, 10),
            Summary(@"D:\素材备份\B", 1, 20),
            Summary(@"E:\素材\A", 1, 30),
            Summary(@"\\nas\share1\素材\A", 1, 40),
            Summary(@"\\nas\share2\素材\A", 1, 50)
        ]);

        Assert.Equal(4, roots.Count);
        var drive = roots.Single(node => node.Path == @"D:\");
        Assert.Equal([@"D:\素材", @"D:\素材备份"], drive.Children.Select(node => node.Path).Order());
        Assert.All(roots.Where(node => node.Path.StartsWith(@"\\nas")),
            node => Assert.Single(AssetDirectoryTree.AllNodes([node])));
        Assert.Equal(150, roots.Sum(node => node.Summary.AvailableSizeBytes));
    }

    [Fact]
    public void Build_MergesWindowsCaseAndTrailingSeparatorVariants()
    {
        var root = Assert.Single(AssetDirectoryTree.Build([
            Summary(@"D:\素材\A", 1, 10),
            Summary(@"d:\素材\a\", 1, 20)
        ]));
        Assert.Equal(2, root.Summary.AssetCount);
        Assert.Equal(30, root.Summary.AvailableSizeBytes);
        Assert.Empty(root.Children);
    }

    [Fact]
    public void VisibleRows_CollapseAndSortSiblingsWithoutBreakingTheHierarchy()
    {
        var roots = AssetDirectoryTree.Build([
            Summary(@"D:\素材\A", 1, 1024),
            Summary(@"D:\素材\A\子目录", 1, 1),
            Summary(@"D:\素材\B", 1, 20)
        ]);
        var root = Assert.Single(roots);
        var first = root.Children.Single(node => node.Path.EndsWith("A"));
        first.IsExpanded = true;
        var ascending = AssetDirectoryTree.GetVisibleRows(roots, sortColumn: 5);

        Assert.Equal([@"D:\素材", @"D:\素材\B", @"D:\素材\A", @"D:\素材\A\子目录"],
            ascending.Select(row => row.Node.Path));
        Assert.Equal([0, 1, 1, 2], ascending.Select(row => row.Depth));
        var descending = AssetDirectoryTree.GetVisibleRows(roots, sortColumn: 5, descending: true);
        Assert.Equal([@"D:\素材", @"D:\素材\A", @"D:\素材\A\子目录", @"D:\素材\B"],
            descending.Select(row => row.Node.Path));
        root.IsExpanded = false;
        Assert.Same(root, Assert.Single(AssetDirectoryTree.GetVisibleRows(roots)).Node);
        root.IsExpanded = true;
        Assert.Equal(4, AssetDirectoryTree.GetVisibleRows(roots).Count);
    }

    [Fact]
    public void PopulateGrid_PreservesFullPathsSelectionAndNumericValues()
    {
        using var grid = new DataGridView { AllowUserToAddRows = false, ReadOnly = true };
        for (var i = 0; i < 7; i++)
        {
            grid.Columns.Add(new DataGridViewTextBoxColumn());
        }
        var roots = AssetDirectoryTree.Build([
            Summary(@"D:\素材", 1, 10), Summary(@"D:\素材\A", 2, 20)
        ]);
        MainForm.PopulateAssetDirectoryTreeGrid(grid,
            AssetDirectoryTree.GetVisibleRows(roots), @"D:\素材\A");

        Assert.Equal(2, grid.Rows.Count);
        Assert.Equal(@"D:\素材", grid.Rows[0].Cells[1].Value);
        Assert.Equal("A", grid.Rows[1].Cells[1].Value);
        Assert.Equal(3L, grid.Rows[0].Cells[2].Value);
        Assert.Equal(30L, grid.Rows[0].Cells[5].Value);
        Assert.Equal(@"D:\素材\A", Assert.IsType<AssetDirectoryTreeNode>(grid.CurrentRow!.Tag).Path);
        Assert.Contains(@"D:\素材\A", grid.Rows[1].Cells[1].ToolTipText);
        Assert.True(grid.Rows[1].Cells[1].Style.Padding.Left > grid.Rows[0].Cells[1].Style.Padding.Left);
        roots[0].IsExpanded = false;
        MainForm.PopulateAssetDirectoryTreeGrid(grid, AssetDirectoryTree.GetVisibleRows(roots), @"D:\素材");
        Assert.Single(grid.Rows.Cast<DataGridViewRow>());
    }

    [Fact]
    public void Build_HandlesAnEmptyLibraryAndSaturatesLargeSpaceTotals()
    {
        Assert.Empty(AssetDirectoryTree.Build([]));
        var root = Assert.Single(AssetDirectoryTree.Build([
            Summary(@"D:\素材\A", 1, long.MaxValue), Summary(@"D:\素材\B", 1, 1)
        ]));
        Assert.Equal(long.MaxValue, root.Summary.AvailableSizeBytes);
    }

    [Fact]
    public void ContextMenu_OffersDirectoryFilteringWithExistingCommands()
    {
        using var menu = new ContextMenuStrip();
        using var open = new ToolStripMenuItem();
        using var remove = new ToolStripMenuItem();
        using var view = new ToolStripMenuItem();
        MainForm.ConfigureAssetDirectoryContextMenu(menu, open, remove, view);
        Assert.Same(view, menu.Items[0]);
        Assert.Equal("查看目录资产（含子目录）", view.Text);
        Assert.Contains(open, menu.Items.Cast<ToolStripItem>());
        Assert.Contains(remove, menu.Items.Cast<ToolStripItem>());
        Assert.Contains("所有子目录", MainForm.CreateAssetDirectoryRemovalConfirmation(@"D:\素材"));
    }

    [Fact]
    public void ContextMenu_SeparatesDirectoryExclusionFromPersistentAssetRemoval()
    {
        using var menu = new ContextMenuStrip();
        using var open = new ToolStripMenuItem();
        using var remove = new ToolStripMenuItem();
        using var view = new ToolStripMenuItem();
        using var removeAssets = new ToolStripMenuItem();
        MainForm.ConfigureAssetDirectoryContextMenu(menu, open, remove, view, removeAssets);
        Assert.Equal("移除目录并移除资产清单", removeAssets.Text);
        Assert.Equal("从扫描目录中移除", remove.Text);
        Assert.Same(removeAssets, menu.Items[menu.Items.Count - 1]);
        Assert.Contains(remove, menu.Items.Cast<ToolStripItem>());
    }

    [Fact]
    public void RemovalConfirmation_ExplainsDescendantsPersistenceAndPreservedProjectMembership()
    {
        var message = MainForm.CreateAssetDirectoryRemovalConfirmation(@"D:\素材", removeAssetRecords: true);
        Assert.Contains(@"D:\素材", message);
        Assert.Contains("所有子目录", message);
        Assert.Contains("持久隐藏", message);
        Assert.Contains("重新添加扫描目录不会自动恢复显示", message);
        Assert.Contains("有其他目录位置的资产保留其他位置的记录", message);
        Assert.Contains("本地文件、资产索引、项目归属和云端备份均保留", message);
        Assert.Contains("不会删除、移动或修改", message);
    }

    private static AssetDirectorySummary Summary(string path, long count, long bytes) =>
        new(path, count, count, 0, bytes, DateTimeOffset.Parse("2026-10-11T08:00:00Z"));
}
