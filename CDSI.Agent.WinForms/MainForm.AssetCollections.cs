using CDSI.Agent.Application.Collections;
using CDSI.Agent.Application.Git;
using CDSI.Agent.Application.Storage;
using CDSI.Agent.Core.Assets;
using CDSI.Agent.Core.Collections;
using CDSI.Agent.Core.Storage;

namespace CDSI.Agent.WinForms;

public sealed partial class MainForm
{
    private readonly AssetCollectionService _assetCollectionService;
    private readonly TabPage _collectionsTabPage = new("项目管理");
    private readonly DataGridView _collectionGrid = new();
    private readonly DataGridView _collectionMemberGrid = new();
    private readonly Button _createCollectionButton = new();
    private readonly Button _syncCollectionButton = new();
    private readonly ToolStripMenuItem _addToCollectionMenuItem = new();
    private readonly ContextMenuStrip _projectContextMenu = new();
    private readonly ToolStripMenuItem _syncProjectContextMenuItem = new();
    private readonly ToolStripMenuItem _syncProjectToGitMenuItem = new();
    private readonly ToolStripMenuItem _deleteProjectContextMenuItem = new();
    private readonly ContextMenuStrip _collectionMemberContextMenu = new();
    private readonly ToolStripMenuItem _removeCollectionMembersMenuItem = new();
    private IReadOnlyList<AssetCollectionSummary> _availableCollections = [];
    private IReadOnlyList<ConfiguredGitProfile> _availableGitProfiles = [];
    private bool _isBusy;
    private bool _refreshingCollections;

    private void ConfigureAssetCollectionTab()
    {
        ConfigureGrid(_collectionGrid);
        _collectionGrid.AccessibleName = "项目列表";
        ConfigureGrid(_collectionMemberGrid);
        _collectionMemberGrid.AccessibleName = "项目内资产列表";
        ConfigureProjectManagementGridColumns(
            _collectionGrid,
            _collectionMemberGrid);

        ConfigureCollectionActionButton(
            _createCollectionButton,
            "新建项目",
            Color.FromArgb(24, 121, 78),
            Color.White);
        ConfigureCollectionActionButton(
            _syncCollectionButton,
            "同步到云端",
            Color.FromArgb(236, 239, 242),
            Color.FromArgb(31, 37, 43));

        _createCollectionButton.Click += async (_, _) => await CreateCollectionAsync();
        _syncCollectionButton.Click += async (_, _) => await SyncSelectedCollectionAsync();
        _collectionGrid.SelectionChanged += CollectionGrid_SelectionChanged;
        _collectionGrid.CellDoubleClick += async (_, args) =>
        {
            if (args.RowIndex < 0 || _isBusy)
            {
                return;
            }

            if (_collectionGrid.Rows[args.RowIndex].Tag is AssetCollectionSummary project)
            {
                await EditCollectionAsync(project);
            }
        };
        _collectionMemberGrid.SelectionChanged += (_, _) =>
            UpdateCollectionActionState();
        ConfigureProjectContextMenu(
            _projectContextMenu,
            _syncProjectContextMenuItem,
            _syncProjectToGitMenuItem,
            _deleteProjectContextMenuItem);
        _syncProjectContextMenuItem.Click += async (_, _) =>
            await SyncSelectedCollectionAsync();
        _deleteProjectContextMenuItem.Click += async (_, _) =>
            await DeleteSelectedProjectAsync();
        _deleteProjectContextMenuItem.ShortcutKeyDisplayString = "Delete";
        _projectContextMenu.Opening += (_, args) =>
        {
            var selectedProjects = GetSelectedCollections();
            args.Cancel = selectedProjects.Count == 0;
            _syncProjectContextMenuItem.Enabled = !_isBusy &&
                selectedProjects.Count == 1;
            ConfigureProjectGitMenu(
                _syncProjectToGitMenuItem,
                _availableGitProfiles,
                !_isBusy && selectedProjects.Count == 1);
            _deleteProjectContextMenuItem.Enabled = !_isBusy &&
                selectedProjects.Count > 0;
        };
        _collectionGrid.ContextMenuStrip = _projectContextMenu;
        _collectionGrid.MouseDown += (_, args) =>
        {
            if (args.Button != MouseButtons.Right)
            {
                return;
            }

            var hit = _collectionGrid.HitTest(args.X, args.Y);
            if (hit.RowIndex >= 0)
            {
                ApplyAssetGridRightClickSelection(
                    _collectionGrid,
                    hit.RowIndex,
                    hit.ColumnIndex,
                    ModifierKeys);
            }
            else
            {
                _collectionGrid.ClearSelection();
                _collectionGrid.CurrentCell = null;
            }
        };

        ConfigureCollectionMemberContextMenu(
            _collectionMemberContextMenu,
            _removeCollectionMembersMenuItem);
        _removeCollectionMembersMenuItem.ForeColor = Color.FromArgb(137, 49, 49);
        _removeCollectionMembersMenuItem.Click += async (_, _) =>
            await RemoveSelectedCollectionMembersAsync();
        _collectionMemberContextMenu.Opening += (_, args) =>
        {
            var hasMembers = GetSelectedCollectionMembers().Count > 0;
            args.Cancel = !hasMembers || GetSelectedCollection() is null;
            _removeCollectionMembersMenuItem.Enabled = !_isBusy && hasMembers;
        };
        _collectionMemberGrid.ContextMenuStrip = _collectionMemberContextMenu;
        _collectionMemberGrid.MouseDown += (_, args) =>
        {
            if (args.Button != MouseButtons.Right)
            {
                return;
            }

            var hit = _collectionMemberGrid.HitTest(args.X, args.Y);
            if (hit.RowIndex >= 0)
            {
                ApplyAssetGridRightClickSelection(
                    _collectionMemberGrid,
                    hit.RowIndex,
                    hit.ColumnIndex,
                    ModifierKeys);
            }
            else
            {
                _collectionMemberGrid.ClearSelection();
                _collectionMemberGrid.CurrentCell = null;
            }
        };

        _collectionsTabPage.Padding = Padding.Empty;
        _collectionsTabPage.BackColor = Color.White;
        _collectionsTabPage.Controls.Add(CreateAssetCollectionLayout(
            _collectionGrid,
            _collectionMemberGrid,
            _createCollectionButton,
            _syncCollectionButton));
        UpdateCollectionActionState();
    }

    internal static void ConfigureProjectContextMenu(
        ContextMenuStrip contextMenu,
        ToolStripMenuItem syncItem,
        ToolStripMenuItem syncToGitItem,
        ToolStripMenuItem deleteItem)
    {
        ArgumentNullException.ThrowIfNull(contextMenu);
        ArgumentNullException.ThrowIfNull(syncItem);
        ArgumentNullException.ThrowIfNull(syncToGitItem);
        ArgumentNullException.ThrowIfNull(deleteItem);
        syncItem.Text = "同步到云端";
        syncToGitItem.Text = "同步到Git";
        deleteItem.Text = "删除项目";
        contextMenu.Items.Clear();
        contextMenu.Items.Add(syncItem);
        contextMenu.Items.Add(syncToGitItem);
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add(deleteItem);
    }

    internal static void ConfigureCollectionMemberContextMenu(
        ContextMenuStrip contextMenu,
        ToolStripMenuItem removeItem)
    {
        ArgumentNullException.ThrowIfNull(contextMenu);
        ArgumentNullException.ThrowIfNull(removeItem);
        removeItem.Text = "移出项目";
        contextMenu.Items.Clear();
        contextMenu.Items.Add(removeItem);
    }

    internal static void ConfigureProjectManagementGridColumns(
        DataGridView projectGrid,
        DataGridView memberGrid)
    {
        ArgumentNullException.ThrowIfNull(projectGrid);
        ArgumentNullException.ThrowIfNull(memberGrid);

        projectGrid.Columns.Add(CreateColumn(
            "名称",
            160,
            DataGridViewAutoSizeColumnMode.Fill,
            45,
            minimumWidth: 120));
        projectGrid.Columns.Add(CreateColumn("类型", 64));
        projectGrid.Columns.Add(CreateColumn("云端备份", 170));
        projectGrid.Columns.Add(CreateColumn("创建时间", 145));
        projectGrid.Columns.Add(CreateColumn("资产", 58));
        projectGrid.Columns.Add(CreateFileSizeColumn());
        projectGrid.Columns.Add(CreateColumn("已备份", 70));

        var resourceIdColumn = CreateAssetIdColumn();
        resourceIdColumn.Name = "ProjectAssetId";
        resourceIdColumn.HeaderText = "资源ID";
        memberGrid.Columns.Add(resourceIdColumn);
        memberGrid.Columns.Add(CreateColumn(
            "文件",
            220,
            DataGridViewAutoSizeColumnMode.Fill,
            36,
            minimumWidth: 160));
        memberGrid.Columns.Add(CreateColumn("类型", 110));
        memberGrid.Columns.Add(CreateFileSizeColumn());
        memberGrid.Columns.Add(CreateColumn("加入时间", 145));
        memberGrid.Columns.Add(CreateColumn(
            "位置",
            280,
            DataGridViewAutoSizeColumnMode.Fill,
            48,
            minimumWidth: 200));
        memberGrid.Columns.Add(CreateBackupStatusColumn());

        EnableFreeColumnResizing(projectGrid);
        EnableFreeColumnResizing(memberGrid);
        EnableAssetMultiSelection(projectGrid);
        EnableAssetMultiSelection(memberGrid);
    }

    internal static Control CreateAssetCollectionLayout(
        DataGridView collectionGrid,
        DataGridView memberGrid,
        Button createButton,
        Button syncButton)
    {
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
        toolbar.Controls.Add(createButton);
        toolbar.Controls.Add(syncButton);
        layout.Controls.Add(toolbar, 0, 0);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            BorderStyle = BorderStyle.None,
            Size = new Size(900, 400)
        };
        split.SplitterDistance = 430;
        split.Panel1MinSize = 300;
        split.Panel2MinSize = 420;
        split.Panel1.Padding = new Padding(0, 0, 6, 0);
        split.Panel2.Padding = new Padding(6, 0, 0, 0);
        split.Panel1.Controls.Add(CreateCollectionPane("项目列表", collectionGrid));
        split.Panel2.Controls.Add(CreateCollectionPane("项目内资产", memberGrid));
        layout.Controls.Add(split, 0, 1);
        return layout;
    }

    private static Control CreateCollectionPane(string title, DataGridView grid)
    {
        var pane = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        pane.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        pane.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        pane.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = title,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(8, 0, 0, 0),
            Font = new Font("Segoe UI Semibold", 9F),
            ForeColor = Color.FromArgb(52, 61, 69),
            BackColor = Color.FromArgb(247, 248, 250)
        }, 0, 0);
        pane.Controls.Add(grid, 0, 1);
        return pane;
    }

    private static void ConfigureCollectionActionButton(
        Button button,
        string text,
        Color background,
        Color foreground)
    {
        button.Text = text;
        button.AutoSize = false;
        button.Size = new Size(128, 32);
        button.Margin = new Padding(0, 0, 8, 0);
        button.BackColor = background;
        button.ForeColor = foreground;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.Cursor = Cursors.Hand;
    }

    private async Task CreateCollectionAsync()
    {
        var collectionId = await CreateCollectionWithDialogAsync();
        if (collectionId is not null)
        {
            await RefreshAssetCollectionsAsync(collectionId);
        }
    }

    private async Task<Guid?> CreateCollectionWithDialogAsync()
    {
        IReadOnlyList<ConfiguredObjectStorageProfile> backupProfiles;
        try
        {
            backupProfiles = await _storageService.ListAsync();
        }
        catch (Exception exception)
        {
            ShowError("无法读取备份配置", exception);
            return null;
        }

        using var dialog = new AssetCollectionDialog(backupProfiles);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return null;
        }

        try
        {
            AssetCollection? collection = null;
            if (!await _stateDatabaseWriteGate.TryRunAsync(async () =>
            {
                collection = await _assetCollectionService.CreateAsync(
                    dialog.CollectionName,
                    dialog.CollectionType,
                    dialog.BackupProfileIds);
            }))
            {
                return null;
            }

            if (collection is null)
            {
                throw new InvalidOperationException("项目创建未返回结果。");
            }

            _statusLabel.Text = $"已创建项目：{collection.Name}";
            return collection.Id;
        }
        catch (Exception exception)
        {
            ShowError("无法创建项目", exception);
            return null;
        }
    }

    private async Task EditCollectionAsync(AssetCollectionSummary project)
    {
        using var dialog = new AssetCollectionDialog(project.Name, project.Type);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        SetBusy(true, allowCancel: false);
        try
        {
            var updated = await _assetCollectionService.UpdateAsync(
                project.Id,
                dialog.CollectionName,
                dialog.CollectionType);
            await RefreshAssetCollectionsAsync(updated.Id);
            await RefreshAssetPageAsync();
            _statusLabel.Text = $"已更新项目：{updated.Name}";
        }
        catch (Exception exception)
        {
            ShowError("无法更新项目", exception);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ConfigureAddToProjectMenu(
        IReadOnlyList<AssetListItem> selectedAssets)
    {
        PopulateAddToProjectMenu(
            _addToCollectionMenuItem,
            _availableCollections,
            selectedAssets.Count);
        foreach (var item in _addToCollectionMenuItem.DropDownItems
                     .OfType<ToolStripMenuItem>())
        {
            item.Click += AddToProjectMenuItem_Click;
        }
    }

    private void ConfigureSyncToProjectMenu(
        IReadOnlyList<AssetListItem> selectedAssets)
    {
        var commonProjects = FindCommonProjects(
            _availableCollections,
            selectedAssets);
        PopulateSyncToProjectMenu(
            _backupToOssMenuItem,
            commonProjects,
            selectedAssets.Count);
        foreach (var item in _backupToOssMenuItem.DropDownItems
                     .OfType<ToolStripMenuItem>())
        {
            item.Click += SyncToProjectMenuItem_Click;
        }
    }

    internal static IReadOnlyList<AssetCollectionSummary> FindCommonProjects(
        IReadOnlyList<AssetCollectionSummary> projects,
        IReadOnlyList<AssetListItem> selectedAssets)
    {
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(selectedAssets);
        if (selectedAssets.Count == 0)
        {
            return [];
        }

        return projects
            .Where(project => selectedAssets.All(asset =>
                asset.ProjectNames.Contains(
                    project.Name,
                    StringComparer.OrdinalIgnoreCase)))
            .ToArray();
    }

    internal static IReadOnlyList<AssetCollectionSummary> FindProjectsForAsset(
        IReadOnlyList<AssetCollectionSummary> projects,
        AssetListItem asset)
    {
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(asset);
        return projects
            .Where(project => asset.ProjectNames.Contains(
                project.Name,
                StringComparer.OrdinalIgnoreCase))
            .ToArray();
    }

    internal static void PopulateSyncToProjectMenu(
        ToolStripMenuItem menuItem,
        IReadOnlyList<AssetCollectionSummary> commonProjects,
        int selectedAssetCount)
    {
        ArgumentNullException.ThrowIfNull(menuItem);
        ArgumentNullException.ThrowIfNull(commonProjects);
        if (selectedAssetCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(selectedAssetCount));
        }

        foreach (var existingItem in menuItem.DropDownItems
                     .Cast<ToolStripItem>()
                     .ToArray())
        {
            existingItem.Dispose();
        }

        menuItem.DropDownItems.Clear();
        menuItem.Text = $"同步到云端 ({selectedAssetCount:N0})";
        menuItem.Enabled = selectedAssetCount > 0;
        if (commonProjects.Count == 0)
        {
            menuItem.DropDownItems.Add(
                new ToolStripMenuItem("加入项目并备份..."));
            return;
        }

        foreach (var project in commonProjects.Take(3))
        {
            menuItem.DropDownItems.Add(new ToolStripMenuItem(
                FormatQuickProjectMenuName(project.Name))
            {
                Tag = project.Id,
                ToolTipText = project.Name
            });
        }

        if (commonProjects.Count > 3)
        {
            menuItem.DropDownItems.Add(new ToolStripSeparator());
            menuItem.DropDownItems.Add(new ToolStripMenuItem("更多..."));
        }
    }

    internal static void PopulateAddToProjectMenu(
        ToolStripMenuItem menuItem,
        IReadOnlyList<AssetCollectionSummary> projects,
        int selectedAssetCount)
    {
        ArgumentNullException.ThrowIfNull(menuItem);
        ArgumentNullException.ThrowIfNull(projects);
        if (selectedAssetCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(selectedAssetCount));
        }

        foreach (var existingItem in menuItem.DropDownItems
                     .Cast<ToolStripItem>()
                     .ToArray())
        {
            existingItem.Dispose();
        }

        menuItem.DropDownItems.Clear();
        menuItem.Text = $"加入项目 ({selectedAssetCount:N0})";
        menuItem.Enabled = selectedAssetCount > 0;
        menuItem.DropDownItems.Add(new ToolStripMenuItem("新建项目")
        {
            Tag = AddToProjectMenuCommand.Create
        });
        if (projects.Count == 0)
        {
            return;
        }

        menuItem.DropDownItems.Add(new ToolStripSeparator());
        foreach (var project in projects.Take(3))
        {
            menuItem.DropDownItems.Add(new ToolStripMenuItem(
                FormatQuickProjectMenuName(project.Name))
            {
                Tag = project.Id,
                ToolTipText = project.Name
            });
        }

        if (projects.Count > 3)
        {
            menuItem.DropDownItems.Add(new ToolStripSeparator());
            menuItem.DropDownItems.Add(new ToolStripMenuItem("更多..."));
        }
    }

    private static string FormatQuickProjectMenuName(string projectName)
    {
        const int maximumDisplayLength = 40;
        var displayName = projectName.Length <= maximumDisplayLength
            ? projectName
            : $"{projectName[..(maximumDisplayLength - 3)]}...";
        return displayName.Replace("&", "&&", StringComparison.Ordinal);
    }

    private async void AddToProjectMenuItem_Click(object? sender, EventArgs e)
    {
        var tag = (sender as ToolStripMenuItem)?.Tag;
        if (tag is Guid projectId)
        {
            await AddSelectedAssetsToCollectionAsync(projectId);
            return;
        }

        if (tag is AddToProjectMenuCommand.Create)
        {
            await AddSelectedAssetsToNewCollectionAsync();
            return;
        }

        await AddSelectedAssetsToCollectionAsync();
    }

    private async void SyncToProjectMenuItem_Click(object? sender, EventArgs e)
    {
        if ((sender as ToolStripMenuItem)?.Tag is Guid projectId)
        {
            await SyncSelectedAssetsToProjectAsync(projectId);
            return;
        }

        if (string.Equals(
                (sender as ToolStripMenuItem)?.Text,
                "加入项目并备份...",
                StringComparison.Ordinal))
        {
            await AddSelectedAssetsToProjectAndSyncAsync();
            return;
        }

        await SyncSelectedAssetsToProjectAsync();
    }

    private async Task OpenCurrentAssetProjectAsync()
    {
        if (_assetGrid.CurrentRow?.Tag is not AssetListItem asset)
        {
            return;
        }

        var projects = FindProjectsForAsset(_availableCollections, asset);
        if (projects.Count == 0)
        {
            return;
        }

        Guid? projectId;
        if (projects.Count == 1)
        {
            projectId = projects[0].Id;
        }
        else
        {
            using var selection = new AssetCollectionSelectionForm(
                projects,
                selectedAssetCount: 1,
                AssetCollectionSelectionPurpose.Open);
            projectId = selection.ShowDialog(this) == DialogResult.OK
                ? selection.SelectedCollectionId
                : null;
        }

        if (projectId is null)
        {
            return;
        }

        try
        {
            _mainTabControl.SelectedTab = _collectionsTabPage;
            await RefreshAssetCollectionsAsync(projectId.Value);
            SelectProjectMember(_collectionMemberGrid, asset.AssetId);
        }
        catch (Exception exception)
        {
            ShowError("无法打开所在项目", exception);
        }
    }

    internal static bool SelectProjectMember(
        DataGridView memberGrid,
        Guid assetId)
    {
        ArgumentNullException.ThrowIfNull(memberGrid);
        var row = memberGrid.Rows
            .Cast<DataGridViewRow>()
            .FirstOrDefault(candidate =>
                (candidate.Tag as AssetCollectionMember)?.Asset.AssetId == assetId);
        if (row is null)
        {
            return false;
        }

        memberGrid.ClearSelection();
        memberGrid.CurrentCell = row.Cells[0];
        row.Selected = true;
        return true;
    }

    private async Task AddSelectedAssetsToCollectionAsync()
    {
        var selectedAssets = GetSelectedAssets();
        if (selectedAssets.Count == 0)
        {
            return;
        }

        try
        {
            var collections = await _assetCollectionService.ListAsync();
            Guid? collectionId;
            if (collections.Count == 0)
            {
                collectionId = await CreateCollectionWithDialogAsync();
            }
            else
            {
                using var selection = new AssetCollectionSelectionForm(
                    collections,
                    selectedAssets.Count);
                collectionId = selection.ShowDialog(this) == DialogResult.OK
                    ? selection.SelectedCollectionId
                    : null;
            }

            if (collectionId is null)
            {
                return;
            }

            await AddSelectedAssetsToCollectionAsync(
                collectionId.Value,
                selectedAssets);
        }
        catch (Exception exception)
        {
            ShowError("无法将资产加入项目", exception);
        }
    }

    private async Task AddSelectedAssetsToNewCollectionAsync()
    {
        var selectedAssets = GetSelectedAssets();
        if (selectedAssets.Count == 0)
        {
            return;
        }

        var collectionId = await CreateCollectionWithDialogAsync();
        if (collectionId is null)
        {
            return;
        }

        try
        {
            await AddSelectedAssetsToCollectionAsync(
                collectionId.Value,
                selectedAssets);
        }
        catch (Exception exception)
        {
            ShowError("无法将资产加入新项目", exception);
        }
    }

    private async Task AddSelectedAssetsToCollectionAsync(Guid collectionId)
    {
        var selectedAssets = GetSelectedAssets();
        if (selectedAssets.Count == 0)
        {
            return;
        }

        try
        {
            await AddSelectedAssetsToCollectionAsync(collectionId, selectedAssets);
        }
        catch (Exception exception)
        {
            ShowError("无法将资产加入项目", exception);
        }
    }

    private async Task AddSelectedAssetsToCollectionAsync(
        Guid collectionId,
        IReadOnlyList<AssetListItem> selectedAssets)
    {
        _ = await _stateDatabaseWriteGate.TryRunAsync(async () =>
        {
            var added = await _assetCollectionService.AddAssetsAsync(
                collectionId,
                selectedAssets.Select(asset => asset.AssetId).ToArray());
            await RefreshAssetCollectionsAsync(collectionId);
            await RefreshAssetPageAsync();
            _statusLabel.Text = added == 0
                ? "所选资产已在该项目中"
                : $"已将 {added:N0} 个资产加入项目";
        });
    }

    private async Task RemoveSelectedCollectionMembersAsync()
    {
        var project = GetSelectedCollection();
        var members = GetSelectedCollectionMembers();
        if (project is null || members.Count == 0)
        {
            return;
        }

        if (MessageBox.Show(
                this,
                CreateCollectionMemberRemovalConfirmation(
                    project,
                    members.Count),
                "移出项目",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.OK)
        {
            return;
        }

        try
        {
            if (!await _stateDatabaseWriteGate.TryRunAsync(async () =>
            {
                var removed = await _assetCollectionService.RemoveAssetsAsync(
                    project.Id,
                    members.Select(member => member.Asset.AssetId).ToArray());
                await RefreshAssetCollectionsAsync(project.Id);
                await RefreshAssetPageAsync();
                _statusLabel.Text =
                    $"已将 {removed:N0} 个资产移出项目“{project.Name}”；本地文件未更改";
            }))
            {
                return;
            }
        }
        catch (Exception exception)
        {
            ShowError("无法将资产移出项目", exception);
        }
    }

    internal static string CreateCollectionMemberRemovalConfirmation(
        AssetCollectionSummary project,
        int memberCount)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(memberCount);
        return
            $"确定将所选 {memberCount:N0} 个资产移出项目“{project.Name}”吗？" +
            $"{Environment.NewLine}{Environment.NewLine}" +
            "只移除项目成员关系，不会删除、移动或修改本地文件，" +
            "不会从全部资产中移除，也不会删除已有云端备份。";
    }

    private async Task SyncSelectedCollectionAsync()
    {
        var selected = GetSelectedCollection();
        if (selected is null)
        {
            return;
        }

        if (!TryBeginStatefulOperation())
        {
            return;
        }

        try
        {
            var plan = await _assetCollectionService.PrepareSyncAsync(selected.Id);
            if (plan.Members.Count == 0)
            {
                MessageBox.Show(
                    this,
                    "该项目还没有资产。",
                    "CDSI Beacon",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            if (plan.UnavailableAssetCount > 0)
            {
                MessageBox.Show(
                    this,
                    $"项目中有 {plan.UnavailableAssetCount:N0} 个本地位置缺失的资产。请恢复这些文件后再同步整个项目。",
                    "CDSI Beacon",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            await BackupProjectAssetsAsync(
                plan.Assets,
                $"正在同步项目：{plan.Collection.Name}",
                plan.Collection.Name,
                plan.Collection.BackupProfileIds);
        }
        catch (Exception exception)
        {
            ShowError("无法同步项目", exception);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task DeleteSelectedProjectAsync()
    {
        var selected = GetSelectedCollections();
        if (selected.Count == 0)
        {
            return;
        }

        var confirmation = MessageBox.Show(
            this,
            CreateProjectsDeletionConfirmation(selected),
            "删除项目",
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (confirmation != DialogResult.OK)
        {
            return;
        }

        SetBusy(true, allowCancel: false);
        var deletedProjects = new List<string>();
        var failures = new List<string>();
        try
        {
            foreach (var project in selected)
            {
                try
                {
                    var deleted = await _assetCollectionService.DeleteAsync(project.Id);
                    deletedProjects.Add(deleted.Name);
                }
                catch (Exception exception)
                {
                    _runtimeLog.WriteError(
                        $"删除项目失败：{project.Name}",
                        exception);
                    failures.Add($"{project.Name}: {exception.Message}");
                }
            }

            await RefreshAssetCollectionsAsync();
            await RefreshAssetPageAsync();
            _statusLabel.Text = failures.Count == 0
                ? $"已删除 {deletedProjects.Count:N0} 个项目；资产文件和云端备份未更改"
                : $"项目删除完成，成功 {deletedProjects.Count:N0} 个，失败 {failures.Count:N0} 个";
            if (failures.Count > 0)
            {
                MessageBox.Show(
                    this,
                    string.Join(Environment.NewLine, failures.Take(8)),
                    "部分项目未删除",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }
        catch (Exception exception)
        {
            ShowError("删除项目后的刷新未能完成", exception);
        }
        finally
        {
            SetBusy(false);
        }
    }

    internal static string CreateProjectsDeletionConfirmation(
        IReadOnlyList<AssetCollectionSummary> projects)
    {
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentOutOfRangeException.ThrowIfZero(projects.Count);
        if (projects.Count == 1)
        {
            return CreateProjectDeletionConfirmation(projects[0]);
        }

        var projectNames = string.Join(
            "、",
            projects.Take(5).Select(project => project.Name));
        if (projects.Count > 5)
        {
            projectNames += $"等 {projects.Count:N0} 个项目";
        }

        return
            $"确定删除所选 {projects.Count:N0} 个项目吗？" +
            $"{Environment.NewLine}{Environment.NewLine}" +
            $"项目：{projectNames}" +
            $"{Environment.NewLine}" +
            $"将移除这些项目以及合计 {projects.Sum(project => project.AssetCount):N0} 条项目成员关系。" +
            $"{Environment.NewLine}{Environment.NewLine}" +
            "不会删除、移动或修改资产文件，也不会删除已有云端备份。" +
            $"{Environment.NewLine}此操作无法撤销。";
    }

    internal static string CreateProjectDeletionConfirmation(
        AssetCollectionSummary project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return
            $"确定删除项目“{project.Name}”吗？{Environment.NewLine}{Environment.NewLine}" +
            $"将移除该项目以及它与 {project.AssetCount:N0} 个资产的本地项目关系。" +
            $"{Environment.NewLine}{Environment.NewLine}" +
            "不会删除、移动或修改资产文件，也不会删除已有云端备份。" +
            $"{Environment.NewLine}此操作无法撤销。";
    }

    private async void CollectionGrid_SelectionChanged(object? sender, EventArgs e)
    {
        if (_refreshingCollections)
        {
            return;
        }

        UpdateCollectionActionState();
        try
        {
            await RefreshSelectedCollectionMembersAsync();
        }
        catch (Exception exception)
        {
            ShowError("无法读取资产清单", exception);
        }
    }

    private async Task RefreshAssetCollectionsAsync(Guid? selectedCollectionId = null)
    {
        var currentId = selectedCollectionId ?? GetSelectedCollection()?.Id;
        var collectionsTask = _assetCollectionService.ListAsync();
        var gitProfilesTask = _gitProfileService.ListAsync();
        await Task.WhenAll(collectionsTask, gitProfilesTask);
        var collections = await collectionsTask;
        _availableCollections = collections;
        _availableGitProfiles = await gitProfilesTask;
        _refreshingCollections = true;
        try
        {
            _collectionGrid.Rows.Clear();
            foreach (var collection in collections)
            {
                var rowIndex = _collectionGrid.Rows.Add(
                    collection.Name,
                    FormatCollectionType(collection.Type),
                    FormatProjectBackupTarget(collection),
                    collection.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                    collection.AssetCount,
                    collection.TotalSizeBytes,
                    $"{collection.BackedUpAssetCount:N0}/{collection.AssetCount:N0}");
                _collectionGrid.Rows[rowIndex].Tag = collection;
            }

            var rowToSelect = _collectionGrid.Rows
                .Cast<DataGridViewRow>()
                .FirstOrDefault(row =>
                    (row.Tag as AssetCollectionSummary)?.Id == currentId)
                ?? _collectionGrid.Rows.Cast<DataGridViewRow>().FirstOrDefault();
            if (rowToSelect is not null)
            {
                _collectionGrid.CurrentCell = rowToSelect.Cells[0];
                rowToSelect.Selected = true;
            }
        }
        finally
        {
            _refreshingCollections = false;
        }

        _collectionsTabPage.Text = $"项目管理 ({collections.Count:N0})";
        await RefreshSelectedCollectionMembersAsync();
        await RefreshGitProjectsAsync();
    }

    internal static string FormatProjectBackupTarget(
        AssetCollectionSummary collection)
    {
        ArgumentNullException.ThrowIfNull(collection);
        if (collection.BackupTargets.Count == 0)
        {
            return "未开启";
        }

        if (collection.BackupTargets.Count == 1)
        {
            var target = collection.BackupTargets[0];
            return $"{FormatProjectBackupProvider(target.Provider)} · {target.ProfileName}";
        }

        var providers = collection.BackupTargets
            .Select(target => FormatProjectBackupProvider(target.Provider))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return $"{collection.BackupTargets.Count:N0} 个目标 · {string.Join("、", providers)}";
    }

    private static string FormatProjectBackupProvider(ObjectStorageProvider provider)
    {
        return provider switch
        {
            ObjectStorageProvider.AliyunOss => "阿里云 OSS",
            ObjectStorageProvider.TencentCos => "腾讯云 COS",
            ObjectStorageProvider.QiniuKodo => "七牛云 Kodo",
            _ => provider.ToString()
        };
    }

    private async Task RefreshSelectedCollectionMembersAsync()
    {
        var collection = GetSelectedCollection();
        if (collection is null)
        {
            _collectionMemberGrid.Rows.Clear();
            UpdateCollectionActionState();
            return;
        }

        var members = await _assetCollectionService.GetMembersAsync(collection.Id);
        if (GetSelectedCollection()?.Id != collection.Id)
        {
            return;
        }

        _collectionMemberGrid.Rows.Clear();
        foreach (var member in members)
        {
            var asset = member.Asset;
            var rowIndex = _collectionMemberGrid.Rows.Add(
                FormatAssetIdForList(asset.AssetId),
                asset.OriginalFilename,
                asset.MimeType ?? "未知",
                asset.Size,
                member.AddedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                asset.Path,
                asset.HasHealthyObjectStorageBackup ? "已备份" : "未备份");
            _collectionMemberGrid.Rows[rowIndex].Tag = member;
            _collectionMemberGrid.Rows[rowIndex]
                .Cells["ProjectAssetId"]
                .ToolTipText = asset.AssetId.ToString("D");
        }

        UpdateCollectionActionState();
    }

    private AssetCollectionSummary? GetSelectedCollection()
    {
        return _collectionGrid.CurrentRow?.Tag as AssetCollectionSummary;
    }

    private IReadOnlyList<AssetCollectionSummary> GetSelectedCollections()
    {
        return _collectionGrid.SelectedRows
            .Cast<DataGridViewRow>()
            .OrderBy(row => row.Index)
            .Select(row => row.Tag)
            .OfType<AssetCollectionSummary>()
            .ToArray();
    }

    private IReadOnlyList<AssetCollectionMember> GetSelectedCollectionMembers()
    {
        return _collectionMemberGrid.SelectedRows
            .Cast<DataGridViewRow>()
            .OrderBy(row => row.Index)
            .Select(row => row.Tag)
            .OfType<AssetCollectionMember>()
            .ToArray();
    }

    private void UpdateCollectionActionState()
    {
        var selectedProjects = GetSelectedCollections();
        var canSync = !_isBusy && selectedProjects.Count == 1;
        _syncCollectionButton.Enabled = canSync;
        _syncProjectContextMenuItem.Enabled = canSync;
        _deleteProjectContextMenuItem.Enabled = !_isBusy &&
            selectedProjects.Count > 0;
        _removeCollectionMembersMenuItem.Enabled = !_isBusy &&
            GetSelectedCollection() is not null &&
            GetSelectedCollectionMembers().Count > 0;
    }

    internal static string FormatCollectionType(AssetCollectionType type)
    {
        return type switch
        {
            AssetCollectionType.Video => "视频",
            AssetCollectionType.Audio => "音频",
            AssetCollectionType.Image => "图片",
            AssetCollectionType.Text => "文字",
            AssetCollectionType.Mixed => "综合",
            _ => type.ToString()
        };
    }

    private enum AddToProjectMenuCommand
    {
        Create
    }
}
