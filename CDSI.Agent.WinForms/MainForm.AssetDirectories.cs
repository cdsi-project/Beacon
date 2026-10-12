using System.Diagnostics;
using CDSI.Agent.Core.Assets;

namespace CDSI.Agent.WinForms;

public sealed partial class MainForm
{
    private readonly TabPage _assetDirectoriesTabPage = new("资产目录");
    private readonly DataGridView _assetDirectoryGrid = new();
    private readonly Button _openAssetDirectoryButton = new();
    private readonly Label _assetDirectorySummaryLabel = new();
    private readonly ContextMenuStrip _assetDirectoryContextMenu = new();
    private readonly ToolStripMenuItem _openAssetDirectoryMenuItem = new();
    private readonly ToolStripMenuItem _removeAssetDirectoryMenuItem = new();
    private readonly ToolStripMenuItem _removeAssetDirectoryAndAssetsMenuItem = new();
    private readonly ToolStripMenuItem _viewAssetDirectoryMenuItem = new();
    private readonly Button _viewAssetDirectoryButton = new();
    private readonly Button _expandAssetDirectoriesButton = new();
    private readonly Button _collapseAssetDirectoriesButton = new();
    private IReadOnlyList<AssetDirectoryTreeNode> _assetDirectoryRoots = [];
    private int _assetDirectorySortColumn = 1;
    private bool _assetDirectorySortDescending;

    private void ConfigureAssetDirectoryTab()
    {
        ConfigureGrid(_assetDirectoryGrid);
        _assetDirectoryGrid.AccessibleName = "资产目录列表";
        _assetDirectoryGrid.Columns.Add(new DataGridViewButtonColumn
        {
            Name = "DirectoryExpansion",
            HeaderText = "",
            Width = 32,
            MinimumWidth = 32,
            Resizable = DataGridViewTriState.False,
            FlatStyle = FlatStyle.Flat,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _assetDirectoryGrid.Columns.Add(CreateColumn(
            "目录",
            420,
            DataGridViewAutoSizeColumnMode.Fill,
            64,
            minimumWidth: 260));
        _assetDirectoryGrid.Columns.Add(CreateDirectoryCountColumn(
            "资产",
            "DirectoryAssetCount"));
        _assetDirectoryGrid.Columns.Add(CreateDirectoryCountColumn(
            "可用",
            "DirectoryAvailableCount"));
        _assetDirectoryGrid.Columns.Add(CreateDirectoryCountColumn(
            "缺失",
            "DirectoryMissingCount"));
        var sizeColumn = CreateFileSizeColumn();
        sizeColumn.HeaderText = "可用空间";
        sizeColumn.Width = 100;
        _assetDirectoryGrid.Columns.Add(sizeColumn);
        _assetDirectoryGrid.Columns.Add(CreateColumn("最近修改", 145));
        foreach (DataGridViewColumn column in _assetDirectoryGrid.Columns.Cast<DataGridViewColumn>().Skip(1))
        {
            column.SortMode = DataGridViewColumnSortMode.Programmatic;
        }
        _assetDirectoryGrid.ColumnHeaderMouseClick += (_, args) =>
        {
            if (args.ColumnIndex <= 0)
            {
                return;
            }
            _assetDirectorySortDescending = args.ColumnIndex == _assetDirectorySortColumn &&
                !_assetDirectorySortDescending;
            _assetDirectorySortColumn = args.ColumnIndex;
            RenderAssetDirectoryTree();
        };
        _assetDirectoryGrid.CellContentClick += (_, args) =>
        {
            if (args.ColumnIndex == 0 && args.RowIndex >= 0 &&
                _assetDirectoryGrid.Rows[args.RowIndex].Tag is AssetDirectoryTreeNode node)
            {
                ToggleAssetDirectoryNode(node);
            }
        };
        _assetDirectoryGrid.KeyDown += AssetDirectoryGrid_KeyDown;

        ConfigureCollectionActionButton(_viewAssetDirectoryButton, "查看目录资产",
            Color.FromArgb(24, 121, 78), Color.White);
        _viewAssetDirectoryButton.Click += async (_, _) => await ViewSelectedAssetDirectoryAsync();
        ConfigureCollectionActionButton(_expandAssetDirectoriesButton, "全部展开",
            Color.FromArgb(236, 239, 242), Color.FromArgb(31, 37, 43));
        ConfigureCollectionActionButton(_collapseAssetDirectoriesButton, "全部折叠",
            Color.FromArgb(236, 239, 242), Color.FromArgb(31, 37, 43));
        _expandAssetDirectoriesButton.Click += (_, _) => SetAssetDirectoriesExpanded(true);
        _collapseAssetDirectoriesButton.Click += (_, _) => SetAssetDirectoriesExpanded(false);

        ConfigureCollectionActionButton(
            _openAssetDirectoryButton,
            "打开目录",
            Color.FromArgb(236, 239, 242),
            Color.FromArgb(31, 37, 43));
        _openAssetDirectoryButton.AccessibleName = "打开选中的资产目录";
        _openAssetDirectoryButton.Click += (_, _) => OpenSelectedAssetDirectory();
        _assetDirectoryGrid.SelectionChanged += (_, _) =>
            UpdateAssetDirectoryActionState();
        _assetDirectoryGrid.CellDoubleClick += (_, args) =>
        {
            if (args.RowIndex >= 0 && args.ColumnIndex > 0)
            {
                if (_assetDirectoryGrid.Rows[args.RowIndex].Tag is AssetDirectoryTreeNode { Children.Count: > 0 } node)
                {
                    ToggleAssetDirectoryNode(node);
                }
                else
                {
                    OpenSelectedAssetDirectory();
                }
            }
        };
        ConfigureAssetDirectoryContextMenu(
            _assetDirectoryContextMenu,
            _openAssetDirectoryMenuItem,
            _removeAssetDirectoryMenuItem,
            _viewAssetDirectoryMenuItem,
            _removeAssetDirectoryAndAssetsMenuItem);
        _viewAssetDirectoryMenuItem.Click += async (_, _) => await ViewSelectedAssetDirectoryAsync();
        _openAssetDirectoryMenuItem.Click += (_, _) => OpenSelectedAssetDirectory();
        _removeAssetDirectoryMenuItem.Click += async (_, _) =>
            await RemoveSelectedAssetDirectoryAsync();
        _removeAssetDirectoryAndAssetsMenuItem.Click += async (_, _) =>
            await RemoveSelectedAssetDirectoryAsync(removeAssetRecords: true);
        _assetDirectoryContextMenu.Opening += (_, args) =>
            args.Cancel = _isBusy || _refreshingAssetPage ||
                _assetDirectoryGrid.CurrentRow?.Tag is not AssetDirectoryTreeNode;
        _assetDirectoryGrid.ContextMenuStrip = _assetDirectoryContextMenu;
        _assetDirectoryGrid.CellMouseDown += (_, args) =>
        {
            if (args.Button == MouseButtons.Right && args.RowIndex >= 0)
            {
                ApplyAssetGridRightClickSelection(
                    _assetDirectoryGrid,
                    args.RowIndex,
                    args.ColumnIndex,
                    Keys.None);
            }
        };

        _assetDirectorySummaryLabel.AutoSize = true;
        _assetDirectorySummaryLabel.Margin = new Padding(8, 8, 0, 0);
        _assetDirectorySummaryLabel.ForeColor = Color.FromArgb(88, 98, 106);
        _assetDirectorySummaryLabel.AccessibleName = "资产目录统计";

        _assetDirectoriesTabPage.Padding = Padding.Empty;
        _assetDirectoriesTabPage.BackColor = Color.White;
        _assetDirectoriesTabPage.Controls.Add(CreateAssetDirectoryLayout(
            _assetDirectoryGrid,
            _openAssetDirectoryButton,
            _assetDirectorySummaryLabel,
            _viewAssetDirectoryButton,
            _expandAssetDirectoriesButton,
            _collapseAssetDirectoriesButton));
        UpdateAssetDirectoryActionState();
    }

    internal static Control CreateAssetDirectoryLayout(
        DataGridView directoryGrid,
        Button openButton,
        Label summaryLabel,
        params Control[] additionalActions)
    {
        directoryGrid.Dock = DockStyle.Fill;
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = Color.White
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Padding = new Padding(8, 10, 8, 8),
            BackColor = Color.White
        };
        toolbar.Controls.AddRange(additionalActions);
        toolbar.Controls.Add(openButton);
        toolbar.Controls.Add(summaryLabel);
        layout.Controls.Add(toolbar, 0, 0);
        layout.Controls.Add(directoryGrid, 0, 1);
        return layout;
    }

    internal static void ConfigureAssetDirectoryContextMenu(
        ContextMenuStrip contextMenu,
        ToolStripMenuItem openItem,
        ToolStripMenuItem removeItem,
        ToolStripMenuItem? viewItem = null,
        ToolStripMenuItem? removeDirectoryAndAssetsItem = null)
    {
        ArgumentNullException.ThrowIfNull(contextMenu);
        ArgumentNullException.ThrowIfNull(openItem);
        ArgumentNullException.ThrowIfNull(removeItem);
        openItem.Text = "打开目录位置";
        removeItem.Text = "从扫描目录中移除";
        contextMenu.Items.Clear();
        contextMenu.Items.AddRange(
            [openItem, new ToolStripSeparator(), removeItem]);
        if (viewItem is not null)
        {
            viewItem.Text = "查看目录资产（含子目录）";
            contextMenu.Items.Insert(0, viewItem);
        }
        if (removeDirectoryAndAssetsItem is not null)
        {
            removeDirectoryAndAssetsItem.Text = "移除目录并移除资产清单";
            contextMenu.Items.Add(removeDirectoryAndAssetsItem);
        }
    }

    private static DataGridViewColumn CreateDirectoryCountColumn(
        string title,
        string name)
    {
        var column = CreateColumn(title, 72);
        column.Name = name;
        column.ValueType = typeof(long);
        column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        return column;
    }

    private void RefreshAssetDirectories(
        IReadOnlyList<AssetDirectorySummary> directories)
    {
        var expanded = AssetDirectoryTree.AllNodes(_assetDirectoryRoots)
            .ToDictionary(node => node.Path, node => node.IsExpanded, StringComparer.OrdinalIgnoreCase);
        _assetDirectoryRoots = AssetDirectoryTree.Build(directories);
        foreach (var node in AssetDirectoryTree.AllNodes(_assetDirectoryRoots))
        {
            if (expanded.TryGetValue(node.Path, out var wasExpanded))
            {
                node.IsExpanded = wasExpanded;
            }
        }
        RenderAssetDirectoryTree();

        var assetCount = directories.Sum(directory => directory.AssetCount);
        var availableCount = directories.Sum(directory => directory.AvailableAssetCount);
        _assetDirectorySummaryLabel.Text =
            $"{directories.Count:N0} 个资源目录 · {assetCount:N0} 个资产位置 · {availableCount:N0} 个可用（父目录含子目录）";
        _assetDirectoriesTabPage.Text = $"资产目录 ({directories.Count:N0})";
        UpdateAssetDirectoryActionState();
    }

    private void UpdateAssetDirectoryActionState()
    {
        _openAssetDirectoryButton.Enabled =
            !_isBusy &&
            _assetDirectoryGrid.CurrentRow?.Tag is AssetDirectoryTreeNode;
        _viewAssetDirectoryButton.Enabled = _viewAssetDirectoryMenuItem.Enabled =
            _openAssetDirectoryButton.Enabled && !_refreshingAssetPage;
        _expandAssetDirectoriesButton.Enabled = _collapseAssetDirectoriesButton.Enabled =
            _assetDirectoryRoots.Count > 0;
    }

    private void OpenSelectedAssetDirectory()
    {
        if (_assetDirectoryGrid.CurrentRow?.Tag is not AssetDirectoryTreeNode directory)
        {
            return;
        }

        if (!Directory.Exists(directory.Path))
        {
            MessageBox.Show(
                this,
                $"目录当前不可用：{Environment.NewLine}{directory.Path}",
                "CDSI Beacon",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        try
        {
            using var process = Process.Start(CreateOpenDirectoryStartInfo(directory.Path));
        }
        catch (Exception exception)
        {
            ShowError("无法打开资产目录", exception);
        }
    }

    private async Task RemoveSelectedAssetDirectoryAsync(bool removeAssetRecords = false)
    {
        var title = removeAssetRecords ? "移除目录并移除资产清单" : "从扫描目录中移除";
        if (_isBusy || _refreshingAssetPage ||
            _assetDirectoryGrid.CurrentRow?.Tag is not AssetDirectoryTreeNode directory ||
            MessageBox.Show(
                this,
                CreateAssetDirectoryRemovalConfirmation(directory.Path, removeAssetRecords),
                title,
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Warning) != DialogResult.OK)
        {
            return;
        }

        if (!TryBeginStatefulOperation(allowCancel: false))
        {
            return;
        }
        try
        {
            AssetDirectoryExclusionResult? result = null;
            if (!await _stateDatabaseWriteGate.TryRunAsync(async () =>
            {
                result = removeAssetRecords
                    ? await _scanRootService.ExcludeAssetDirectoryAndHideAssetsAsync(directory.Path)
                    : await _scanRootService.ExcludeAssetDirectoryAsync(directory.Path);
            }))
            {
                return;
            }
            await RefreshAssetsAsync();
            _statusLabel.Text = removeAssetRecords
                ? $"已移除目录，排除 {result!.ExcludedLocationCount:N0} 个资产位置，持久移除 {result.HiddenAssetCount:N0} 条资产记录；本地文件、项目归属和云端备份均保留"
                : $"已从扫描目录中移除，排除 {result!.ExcludedLocationCount:N0} 个资产位置，本地文件未删除";
        }
        catch (Exception exception)
        {
            ShowError($"无法完成“{title}”", exception);
        }
        finally
        {
            SetBusy(false);
        }
    }

    internal static string CreateAssetDirectoryRemovalConfirmation(string path, bool removeAssetRecords = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (removeAssetRecords)
        {
            return $"确定移除目录并移除资产清单吗？{Environment.NewLine}{Environment.NewLine}" +
                $"{path}{Environment.NewLine}{Environment.NewLine}" +
                "操作范围包含此目录及其所有子目录；移除后不再扫描，并从“全部资产”中移除这些位置的记录。" +
                Environment.NewLine +
                "仅位于此范围的资产会持久隐藏，重新添加扫描目录不会自动恢复显示；有其他目录位置的资产保留其他位置的记录。" +
                Environment.NewLine +
                "本地文件、资产索引、项目归属和云端备份均保留，不会删除、移动或修改本地文件。是否继续？";
        }
        return $"从扫描目录中移除后不再扫描，也不计入资源清单。{Environment.NewLine}{Environment.NewLine}" +
            $"{path}{Environment.NewLine}{Environment.NewLine}" +
            "操作范围包含此目录及其所有子目录。" + Environment.NewLine +
            "不会删除、移动或修改目录中的本地文件。是否继续？";
    }

    private void RenderAssetDirectoryTree(string? selectedPath = null)
    {
        selectedPath ??= (_assetDirectoryGrid.CurrentRow?.Tag as AssetDirectoryTreeNode)?.Path;
        _assetDirectoryGrid.SuspendLayout();
        try
        {
            PopulateAssetDirectoryTreeGrid(_assetDirectoryGrid,
                AssetDirectoryTree.GetVisibleRows(_assetDirectoryRoots,
                    _assetDirectorySortColumn, _assetDirectorySortDescending), selectedPath);
            foreach (DataGridViewColumn column in _assetDirectoryGrid.Columns)
            {
                column.HeaderCell.SortGlyphDirection = column.Index == _assetDirectorySortColumn
                    ? (_assetDirectorySortDescending ? SortOrder.Descending : SortOrder.Ascending)
                    : SortOrder.None;
            }
        }
        finally
        {
            _assetDirectoryGrid.ResumeLayout();
        }
        UpdateAssetDirectoryActionState();
    }

    internal static void PopulateAssetDirectoryTreeGrid(DataGridView grid,
        IReadOnlyList<AssetDirectoryTreeRow> rows, string? selectedPath = null)
    {
        grid.Rows.Clear();
        DataGridViewRow? selectedRow = null;
        foreach (var entry in rows)
        {
            var node = entry.Node;
            var directory = node.Summary;
            var rowIndex = grid.Rows.Add(
                node.Children.Count == 0 ? "" : node.IsExpanded ? "−" : "+",
                entry.Depth == 0 ? node.Path : Path.GetFileName(Path.TrimEndingDirectorySeparator(node.Path)),
                directory.AssetCount, directory.AvailableAssetCount, directory.MissingAssetCount,
                directory.AvailableSizeBytes,
                directory.LatestModifiedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
            var row = grid.Rows[rowIndex];
            row.Tag = node;
            row.Cells[1].Style.Padding = new Padding(4 + entry.Depth * 20, 0, 4, 0);
            row.Cells[1].ToolTipText = node.Path + Environment.NewLine + "统计包含此目录及其所有子目录";
            if (string.Equals(node.Path, selectedPath, StringComparison.OrdinalIgnoreCase))
            {
                selectedRow = row;
            }
        }
        selectedRow ??= grid.Rows.Cast<DataGridViewRow>().FirstOrDefault();
        grid.ClearSelection();
        if (selectedRow is not null)
        {
            grid.CurrentCell = selectedRow.Cells[1];
            selectedRow.Selected = true;
        }
    }

    private void ToggleAssetDirectoryNode(AssetDirectoryTreeNode node)
    {
        if (node.Children.Count > 0)
        {
            node.IsExpanded = !node.IsExpanded;
            RenderAssetDirectoryTree(node.Path);
        }
    }

    private void SetAssetDirectoriesExpanded(bool expanded)
    {
        foreach (var node in AssetDirectoryTree.AllNodes(_assetDirectoryRoots))
        {
            node.IsExpanded = expanded;
        }
        RenderAssetDirectoryTree();
    }

    private void AssetDirectoryGrid_KeyDown(object? sender, KeyEventArgs args)
    {
        if (_assetDirectoryGrid.CurrentRow?.Tag is not AssetDirectoryTreeNode node ||
            args.KeyCode is not (Keys.Left or Keys.Right or Keys.Space))
        {
            return;
        }
        args.SuppressKeyPress = true;
        if (args.KeyCode == Keys.Space)
        {
            ToggleAssetDirectoryNode(node);
        }
        else if (args.KeyCode == Keys.Right && node.Children.Count > 0)
        {
            node.IsExpanded = true;
            RenderAssetDirectoryTree(node.Path);
        }
        else if (args.KeyCode == Keys.Left)
        {
            var parentVisible = _assetDirectoryGrid.Rows.Cast<DataGridViewRow>()
                .Any(row => ReferenceEquals(row.Tag, node.Parent));
            var path = node.IsExpanded || !parentVisible ? node.Path : node.Parent!.Path;
            node.IsExpanded = false;
            RenderAssetDirectoryTree(path);
        }
    }

    private async Task ViewSelectedAssetDirectoryAsync()
    {
        if (_isBusy || _refreshingAssetPage ||
            _assetDirectoryGrid.CurrentRow?.Tag is not AssetDirectoryTreeNode node)
        {
            return;
        }
        var filter = _assetListFilter;
        _assetListFilter = new AssetListFilter(filter.FileType, filter.CreatedFrom, filter.CreatedBefore,
            filter.Extension, filter.TagId, filter.FilenameContains, node.Path);
        _assetPageIndex = 0;
        _mainTabControl.SelectedTab = _assetsTabPage;
        await RefreshAssetPageAsync();
    }

    internal static ProcessStartInfo CreateOpenDirectoryStartInfo(string directoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        return new ProcessStartInfo
        {
            FileName = Path.GetFullPath(directoryPath),
            UseShellExecute = true
        };
    }
}
