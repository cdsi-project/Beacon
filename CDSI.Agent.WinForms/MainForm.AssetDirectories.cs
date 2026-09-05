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

    private void ConfigureAssetDirectoryTab()
    {
        ConfigureGrid(_assetDirectoryGrid);
        _assetDirectoryGrid.AccessibleName = "资产目录列表";
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
            if (args.RowIndex >= 0)
            {
                OpenSelectedAssetDirectory();
            }
        };
        ConfigureAssetDirectoryContextMenu(
            _assetDirectoryContextMenu,
            _openAssetDirectoryMenuItem,
            _removeAssetDirectoryMenuItem);
        _openAssetDirectoryMenuItem.Click += (_, _) => OpenSelectedAssetDirectory();
        _removeAssetDirectoryMenuItem.Click += async (_, _) =>
            await RemoveSelectedAssetDirectoryAsync();
        _assetDirectoryContextMenu.Opening += (_, args) =>
            args.Cancel = _isBusy ||
                _assetDirectoryGrid.CurrentRow?.Tag is not AssetDirectorySummary;
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
            _assetDirectorySummaryLabel));
        UpdateAssetDirectoryActionState();
    }

    internal static Control CreateAssetDirectoryLayout(
        DataGridView directoryGrid,
        Button openButton,
        Label summaryLabel)
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
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(8, 10, 8, 8),
            BackColor = Color.White
        };
        toolbar.Controls.Add(openButton);
        toolbar.Controls.Add(summaryLabel);
        layout.Controls.Add(toolbar, 0, 0);
        layout.Controls.Add(directoryGrid, 0, 1);
        return layout;
    }

    internal static void ConfigureAssetDirectoryContextMenu(
        ContextMenuStrip contextMenu,
        ToolStripMenuItem openItem,
        ToolStripMenuItem removeItem)
    {
        ArgumentNullException.ThrowIfNull(contextMenu);
        ArgumentNullException.ThrowIfNull(openItem);
        ArgumentNullException.ThrowIfNull(removeItem);
        openItem.Text = "打开目录位置";
        removeItem.Text = "从扫描目录中移除";
        contextMenu.Items.Clear();
        contextMenu.Items.AddRange(
            [openItem, new ToolStripSeparator(), removeItem]);
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
        var selectedPath =
            (_assetDirectoryGrid.CurrentRow?.Tag as AssetDirectorySummary)?.Path;
        _assetDirectoryGrid.Rows.Clear();

        DataGridViewRow? selectedRow = null;
        foreach (var directory in directories)
        {
            var rowIndex = _assetDirectoryGrid.Rows.Add(
                directory.Path,
                directory.AssetCount,
                directory.AvailableAssetCount,
                directory.MissingAssetCount,
                directory.AvailableSizeBytes,
                directory.LatestModifiedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
            var row = _assetDirectoryGrid.Rows[rowIndex];
            row.Tag = directory;
            if (string.Equals(directory.Path, selectedPath, StringComparison.OrdinalIgnoreCase))
            {
                selectedRow = row;
            }
        }

        selectedRow ??= _assetDirectoryGrid.Rows
            .Cast<DataGridViewRow>()
            .FirstOrDefault();
        if (selectedRow is not null)
        {
            _assetDirectoryGrid.CurrentCell = selectedRow.Cells[0];
            selectedRow.Selected = true;
        }

        var assetCount = directories.Sum(directory => directory.AssetCount);
        var availableCount = directories.Sum(directory => directory.AvailableAssetCount);
        _assetDirectorySummaryLabel.Text =
            $"{directories.Count:N0} 个目录 · {assetCount:N0} 个资产位置 · {availableCount:N0} 个可用";
        _assetDirectoriesTabPage.Text = $"资产目录 ({directories.Count:N0})";
        UpdateAssetDirectoryActionState();
    }

    private void UpdateAssetDirectoryActionState()
    {
        _openAssetDirectoryButton.Enabled =
            !_isBusy &&
            _assetDirectoryGrid.CurrentRow?.Tag is AssetDirectorySummary;
    }

    private void OpenSelectedAssetDirectory()
    {
        if (_assetDirectoryGrid.CurrentRow?.Tag is not AssetDirectorySummary directory)
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

    private async Task RemoveSelectedAssetDirectoryAsync()
    {
        if (_assetDirectoryGrid.CurrentRow?.Tag is not AssetDirectorySummary directory ||
            MessageBox.Show(
                this,
                CreateAssetDirectoryRemovalConfirmation(directory.Path),
                "从扫描目录中移除",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Warning) != DialogResult.OK)
        {
            return;
        }

        SetBusy(true, allowCancel: false);
        try
        {
            var result = await _scanRootService.ExcludeAssetDirectoryAsync(directory.Path);
            await RefreshAssetsAsync();
            _statusLabel.Text =
                $"已从扫描目录中移除，排除 {result.ExcludedLocationCount:N0} 个资产位置，本地文件未删除";
        }
        catch (Exception exception)
        {
            ShowError("无法从扫描目录中移除", exception);
        }
        finally
        {
            SetBusy(false);
        }
    }

    internal static string CreateAssetDirectoryRemovalConfirmation(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return $"从扫描目录中移除后不再扫描，也不计入资源清单。{Environment.NewLine}{Environment.NewLine}" +
            $"{path}{Environment.NewLine}{Environment.NewLine}" +
            "不会删除、移动或修改目录中的本地文件。是否继续？";
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
