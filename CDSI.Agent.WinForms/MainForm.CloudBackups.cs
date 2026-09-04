using CDSI.Agent.Application.Storage;
using CDSI.Agent.Core.Storage;

namespace CDSI.Agent.WinForms;

public sealed partial class MainForm
{
    private readonly TabPage _cloudBackupsTabPage = new("云备份管理");
    private readonly DataGridView _cloudBackupProjectGrid = new();
    private readonly DataGridView _cloudBackupGrid = new();
    private readonly TextBox _cloudBackupSearchTextBox = new();
    private readonly Button _searchCloudBackupsButton = new();
    private readonly Button _refreshCloudBackupsButton = new();
    private readonly Label _cloudBackupSummaryLabel = new();
    private readonly ContextMenuStrip _cloudBackupProjectContextMenu = new();
    private readonly ToolStripMenuItem _deleteCloudBackupProjectMenuItem = new();
    private readonly ContextMenuStrip _cloudBackupContextMenu = new();
    private readonly ToolStripMenuItem _openCloudBackupFileLocationMenuItem = new();
    private readonly ToolStripMenuItem _restoreCloudBackupMenuItem = new();
    private readonly ToolStripMenuItem _deleteCloudBackupMenuItem = new();
    private IReadOnlyList<ManagedObjectStorageBackup> _availableCloudBackups = [];
    private bool _refreshingCloudBackupProjects;

    private void ConfigureCloudBackupManagementTab()
    {
        ConfigureGrid(_cloudBackupProjectGrid);
        _cloudBackupProjectGrid.AccessibleName = "云备份项目列表";
        ConfigureGrid(_cloudBackupGrid);
        EnableAssetMultiSelection(_cloudBackupGrid);
        _cloudBackupGrid.AccessibleName = "云备份列表";
        ConfigureCloudBackupManagementGridColumns(
            _cloudBackupProjectGrid,
            _cloudBackupGrid);

        _cloudBackupSearchTextBox.Width = 240;
        _cloudBackupSearchTextBox.Margin = new Padding(0, 3, 0, 0);
        _cloudBackupSearchTextBox.PlaceholderText = "搜索文件、项目、对象键或配置";
        _cloudBackupSearchTextBox.AccessibleName = "云备份搜索";
        _cloudBackupSearchTextBox.KeyDown += (_, eventArgs) =>
        {
            if (eventArgs.KeyCode != Keys.Enter)
            {
                return;
            }

            eventArgs.SuppressKeyPress = true;
            ApplyCloudBackupSearchWithFeedback();
        };
        ConfigureFilterButton(
            _searchCloudBackupsButton,
            "搜索",
            Color.FromArgb(24, 121, 78),
            Color.White);
        ConfigureCollectionActionButton(
            _refreshCloudBackupsButton,
            "刷新",
            Color.FromArgb(236, 239, 242),
            Color.FromArgb(31, 37, 43));
        _refreshCloudBackupsButton.Width = 88;

        _searchCloudBackupsButton.Click += (_, _) =>
            ApplyCloudBackupSearchWithFeedback();
        _refreshCloudBackupsButton.Click += async (_, _) =>
            await RefreshCloudBackupsWithFeedbackAsync();
        _cloudBackupProjectGrid.SelectionChanged += (_, _) =>
        {
            if (!_refreshingCloudBackupProjects)
            {
                RefreshSelectedCloudBackupProject();
            }
        };
        ConfigureCloudBackupProjectContextMenu(
            _cloudBackupProjectContextMenu,
            _deleteCloudBackupProjectMenuItem);
        _deleteCloudBackupProjectMenuItem.ForeColor = Color.FromArgb(137, 49, 49);
        _deleteCloudBackupProjectMenuItem.Click += async (_, _) =>
            await DeleteSelectedCloudBackupProjectAsync();
        _cloudBackupProjectContextMenu.Opening += (_, args) =>
        {
            var project = GetSelectedCloudBackupProject();
            args.Cancel = project is null;
            _deleteCloudBackupProjectMenuItem.Enabled = !_isBusy &&
                project is not null &&
                project.Backups.All(backup => backup.IsAvailable);
        };
        _cloudBackupProjectGrid.ContextMenuStrip = _cloudBackupProjectContextMenu;
        _cloudBackupProjectGrid.MouseDown += (_, args) =>
        {
            if (args.Button != MouseButtons.Right)
            {
                return;
            }

            var hit = _cloudBackupProjectGrid.HitTest(args.X, args.Y);
            if (hit.RowIndex >= 0)
            {
                ApplyAssetGridRightClickSelection(
                    _cloudBackupProjectGrid,
                    hit.RowIndex,
                    hit.ColumnIndex,
                    Keys.None);
            }
        };
        _cloudBackupGrid.SelectionChanged += (_, _) =>
            UpdateCloudBackupActionState();
        _cloudBackupGrid.CellDoubleClick += (_, args) =>
        {
            if (args.RowIndex >= 0)
            {
                OpenCurrentCloudBackupFileLocation();
            }
        };

        _openCloudBackupFileLocationMenuItem.Click += (_, _) =>
            OpenCurrentCloudBackupFileLocation();
        _restoreCloudBackupMenuItem.Click += async (_, _) =>
            await RestoreSelectedCloudBackupsAsync();
        _deleteCloudBackupMenuItem.ForeColor = Color.FromArgb(137, 49, 49);
        _deleteCloudBackupMenuItem.Click += async (_, _) =>
            await DeleteSelectedCloudBackupsAsync();
        ConfigureCloudBackupContextMenu(
            _cloudBackupContextMenu,
            _openCloudBackupFileLocationMenuItem,
            _restoreCloudBackupMenuItem,
            _deleteCloudBackupMenuItem);
        _cloudBackupContextMenu.Opening += (_, args) =>
        {
            var selected = GetSelectedCloudBackups();
            args.Cancel = selected.Count == 0;
            var localPath = GetCloudBackupLocalPath(_cloudBackupGrid.CurrentRow);
            _openCloudBackupFileLocationMenuItem.Enabled =
                !_isBusy && localPath is not null && File.Exists(localPath);
            _restoreCloudBackupMenuItem.Enabled = !_isBusy &&
                selected.All(IsRestorableCloudBackup);
            _deleteCloudBackupMenuItem.Enabled = !_isBusy &&
                selected.All(item => item.IsAvailable);
        };
        _cloudBackupGrid.ContextMenuStrip = _cloudBackupContextMenu;
        _cloudBackupGrid.MouseDown += (_, args) =>
        {
            if (args.Button != MouseButtons.Right)
            {
                return;
            }

            var hit = _cloudBackupGrid.HitTest(args.X, args.Y);
            if (hit.RowIndex >= 0)
            {
                ApplyAssetGridRightClickSelection(
                    _cloudBackupGrid,
                    hit.RowIndex,
                    hit.ColumnIndex,
                    ModifierKeys);
            }
        };

        _cloudBackupSummaryLabel.AutoSize = true;
        _cloudBackupSummaryLabel.Margin = new Padding(8, 8, 0, 0);
        _cloudBackupSummaryLabel.ForeColor = Color.FromArgb(88, 98, 106);
        _cloudBackupSummaryLabel.AccessibleName = "云备份统计";

        _cloudBackupsTabPage.Padding = Padding.Empty;
        _cloudBackupsTabPage.BackColor = Color.White;
        _cloudBackupsTabPage.Controls.Add(CreateCloudBackupLayout());
        UpdateCloudBackupActionState();
    }

    internal static void ConfigureCloudBackupProjectContextMenu(
        ContextMenuStrip contextMenu,
        ToolStripMenuItem deleteProjectItem)
    {
        ArgumentNullException.ThrowIfNull(contextMenu);
        ArgumentNullException.ThrowIfNull(deleteProjectItem);
        deleteProjectItem.Text = "删除备份项目";
        contextMenu.Items.Clear();
        contextMenu.Items.Add(deleteProjectItem);
    }

    internal static void ConfigureCloudBackupManagementGridColumns(
        DataGridView projectGrid,
        DataGridView backupGrid)
    {
        ArgumentNullException.ThrowIfNull(projectGrid);
        ArgumentNullException.ThrowIfNull(backupGrid);

        projectGrid.Columns.Add(CreateColumn(
            "项目",
            180,
            DataGridViewAutoSizeColumnMode.Fill,
            45,
            minimumWidth: 130));
        projectGrid.Columns.Add(CreateColumn("云提供商", 150));
        projectGrid.Columns.Add(CreateColumn("项目资源数", 88));
        projectGrid.Columns.Add(CreateColumn("云端副本", 78));
        var projectSizeColumn = CreateFileSizeColumn();
        projectSizeColumn.HeaderText = "占用空间";
        projectGrid.Columns.Add(projectSizeColumn);
        projectGrid.Columns.Add(CreateColumn("最近备份", 145));

        backupGrid.Columns.Add(CreateColumn(
            "本地资产",
            180,
            DataGridViewAutoSizeColumnMode.Fill,
            30,
            minimumWidth: 140));
        backupGrid.Columns.Add(CreateColumn("所属项目", 150));
        backupGrid.Columns.Add(CreateColumn(
            "云端文件名",
            180,
            DataGridViewAutoSizeColumnMode.Fill,
            30,
            minimumWidth: 140));
        backupGrid.Columns.Add(CreateColumn(
            "对象键",
            280,
            DataGridViewAutoSizeColumnMode.Fill,
            45,
            minimumWidth: 200));
        var backupSizeColumn = CreateFileSizeColumn();
        backupSizeColumn.HeaderText = "大小";
        backupGrid.Columns.Add(backupSizeColumn);
        backupGrid.Columns.Add(CreateColumn("校验状态", 90));
        backupGrid.Columns.Add(CreateColumn("备份时间", 145));

        EnableFreeColumnResizing(projectGrid);
        EnableFreeColumnResizing(backupGrid);
    }

    internal static void ConfigureCloudBackupContextMenu(
        ContextMenuStrip contextMenu,
        ToolStripMenuItem openFileLocationItem,
        ToolStripMenuItem restoreItem,
        ToolStripMenuItem deleteItem)
    {
        ArgumentNullException.ThrowIfNull(contextMenu);
        ArgumentNullException.ThrowIfNull(openFileLocationItem);
        ArgumentNullException.ThrowIfNull(restoreItem);
        ArgumentNullException.ThrowIfNull(deleteItem);

        openFileLocationItem.Text = "打开文件位置";
        restoreItem.Text = "取回";
        deleteItem.Text = "删除备份";
        contextMenu.Items.AddRange(
        [
            openFileLocationItem,
            new ToolStripSeparator(),
            restoreItem,
            new ToolStripSeparator(),
            deleteItem
        ]);
    }

    private Control CreateCloudBackupLayout()
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
        toolbar.Controls.Add(_cloudBackupSearchTextBox);
        toolbar.Controls.Add(_searchCloudBackupsButton);
        toolbar.Controls.Add(_refreshCloudBackupsButton);
        toolbar.Controls.Add(_cloudBackupSummaryLabel);
        layout.Controls.Add(toolbar, 0, 0);

        layout.Controls.Add(CreateCloudBackupProjectSplit(
            _cloudBackupProjectGrid,
            _cloudBackupGrid), 0, 1);
        return layout;
    }

    internal static SplitContainer CreateCloudBackupProjectSplit(
        DataGridView projectGrid,
        DataGridView backupGrid)
    {
        ArgumentNullException.ThrowIfNull(projectGrid);
        ArgumentNullException.ThrowIfNull(backupGrid);
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            BorderStyle = BorderStyle.None,
            Size = new Size(900, 400),
            SplitterDistance = 390,
            Panel1MinSize = 280,
            Panel2MinSize = 480
        };
        split.Panel1.Padding = new Padding(0, 0, 6, 0);
        split.Panel2.Padding = new Padding(6, 0, 0, 0);
        split.Panel1.Controls.Add(CreateCollectionPane(
            "备份项目",
            projectGrid));
        split.Panel2.Controls.Add(CreateCollectionPane(
            "云端备份",
            backupGrid));
        return split;
    }

    private async Task RefreshCloudBackupsAsync()
    {
        _availableCloudBackups = await _objectStorageManagementService.ListAsync();
        ApplyCloudBackupSearch();
    }

    private void ApplyCloudBackupSearchWithFeedback()
    {
        var visibleCount = ApplyCloudBackupSearch();
        _statusLabel.Text = string.IsNullOrWhiteSpace(_cloudBackupSearchTextBox.Text)
            ? "已显示全部云备份"
            : $"云备份搜索完成，找到 {visibleCount:N0} 个结果";
    }

    private int ApplyCloudBackupSearch()
    {
        var selectedProject = GetSelectedCloudBackupProject();
        var selectedIds = GetSelectedCloudBackups()
            .Select(item => item.Source.Location.Id)
            .ToHashSet();
        var backups = FilterCloudBackups(
            _availableCloudBackups,
            _cloudBackupSearchTextBox.Text);
        var projects = GroupCloudBackupsByProject(backups);

        _refreshingCloudBackupProjects = true;
        try
        {
            _cloudBackupProjectGrid.Rows.Clear();
            foreach (var project in projects)
            {
                var rowIndex = _cloudBackupProjectGrid.Rows.Add(
                    project.Name,
                    FormatCloudBackupProjectProviders(project),
                    project.AssetCount,
                    project.Backups.Count,
                    project.TotalSizeBytes,
                    project.LatestBackupAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
                _cloudBackupProjectGrid.Rows[rowIndex].Tag = project;
            }

            var rowToSelect = _cloudBackupProjectGrid.Rows
                .Cast<DataGridViewRow>()
                .FirstOrDefault(row =>
                    row.Tag is CloudBackupProjectGroup project &&
                    selectedProject is not null &&
                    project.IsUnassigned == selectedProject.IsUnassigned &&
                    string.Equals(
                        project.Name,
                        selectedProject.Name,
                        StringComparison.OrdinalIgnoreCase))
                ?? _cloudBackupProjectGrid.Rows
                    .Cast<DataGridViewRow>()
                    .FirstOrDefault();
            if (rowToSelect is not null)
            {
                _cloudBackupProjectGrid.CurrentCell = rowToSelect.Cells[0];
                rowToSelect.Selected = true;
            }
        }
        finally
        {
            _refreshingCloudBackupProjects = false;
        }

        RefreshSelectedCloudBackupProject(selectedIds);
        _cloudBackupsTabPage.Text = $"云备份管理 ({_availableCloudBackups.Count:N0})";
        return backups.Count;
    }

    private void RefreshSelectedCloudBackupProject(
        IReadOnlySet<Guid>? selectedIds = null)
    {
        selectedIds ??= GetSelectedCloudBackups()
            .Select(item => item.Source.Location.Id)
            .ToHashSet();
        var project = GetSelectedCloudBackupProject();
        _cloudBackupGrid.Rows.Clear();
        if (project is null)
        {
            _cloudBackupSummaryLabel.Text = "没有符合条件的云端副本";
            UpdateCloudBackupActionState();
            return;
        }

        foreach (var backup in project.Backups)
        {
            var source = backup.Source;
            var location = source.Location;
            var rowIndex = _cloudBackupGrid.Rows.Add(
                source.OriginalFilename,
                project.Name,
                GetCloudFilename(location.ObjectKey),
                location.ObjectKey,
                location.Size,
                FormatCloudVerificationStatus(location.Status),
                location.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
            var row = _cloudBackupGrid.Rows[rowIndex];
            row.Tag = backup;
            if (!backup.IsAvailable)
            {
                row.DefaultCellStyle.ForeColor = Color.FromArgb(137, 49, 49);
            }

            row.Selected = selectedIds.Contains(location.Id);
        }

        _cloudBackupSummaryLabel.Text =
            $"{project.Name} · {project.AssetCount:N0} 个资源 · " +
            $"{project.Backups.Count:N0} 个副本 · {FormatFileSize(project.TotalSizeBytes)}";
        UpdateCloudBackupActionState();
    }

    private CloudBackupProjectGroup? GetSelectedCloudBackupProject()
    {
        return _cloudBackupProjectGrid.CurrentRow?.Tag as CloudBackupProjectGroup;
    }

    internal static IReadOnlyList<ManagedObjectStorageBackup> FilterCloudBackups(
        IEnumerable<ManagedObjectStorageBackup> backups,
        string? searchText)
    {
        ArgumentNullException.ThrowIfNull(backups);
        var term = searchText?.Trim();
        if (string.IsNullOrEmpty(term))
        {
            return backups.ToArray();
        }

        return backups.Where(backup =>
        {
            var source = backup.Source;
            var location = source.Location;
            string?[] candidates =
            [
                source.OriginalFilename,
                GetCloudFilename(location.ObjectKey),
                location.ObjectKey,
                backup.Profile?.DisplayName,
                FormatCloudBackupProvider(backup.Profile?.Provider),
                source.LocalPath,
                FormatCloudBackupProjectNames(source.ProjectNames)
            ];
            return candidates.Any(candidate =>
                candidate?.Contains(term, StringComparison.OrdinalIgnoreCase) == true);
        }).ToArray();
    }

    internal static IReadOnlyList<CloudBackupProjectGroup>
        GroupCloudBackupsByProject(
            IEnumerable<ManagedObjectStorageBackup> backups)
    {
        ArgumentNullException.ThrowIfNull(backups);
        var groups = new Dictionary<string, List<ManagedObjectStorageBackup>>(
            StringComparer.OrdinalIgnoreCase);
        var unassigned = new List<ManagedObjectStorageBackup>();

        foreach (var backup in backups)
        {
            var projectName = GetCloudBackupProjectName(
                backup.Source.Location.ObjectKey);
            if (projectName is null)
            {
                unassigned.Add(backup);
                continue;
            }

            if (!groups.TryGetValue(projectName, out var projectBackups))
            {
                projectBackups = [];
                groups.Add(projectName, projectBackups);
            }

            projectBackups.Add(backup);
        }

        var result = groups
            .OrderBy(pair => pair.Key, StringComparer.CurrentCultureIgnoreCase)
            .Select(pair => new CloudBackupProjectGroup(
                pair.Key,
                false,
                pair.Value.ToArray()))
            .ToList();
        if (unassigned.Count > 0)
        {
            result.Add(new CloudBackupProjectGroup(
                "未归属项目",
                true,
                unassigned.ToArray()));
        }

        return result;
    }

    internal static string? GetCloudBackupProjectName(string objectKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);
        var separatorIndex = objectKey.LastIndexOf('/');
        if (separatorIndex <= 0)
        {
            return null;
        }

        var directory = objectKey[..separatorIndex].Trim();
        return directory.Length == 0 || directory.Contains('/')
            ? null
            : directory;
    }

    internal static string FormatCloudBackupProjectProviders(
        CloudBackupProjectGroup project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return string.Join(
            "、",
            project.Backups
                .Select(backup =>
                    FormatCloudBackupProvider(backup.Profile?.Provider))
                .Distinct(StringComparer.Ordinal));
    }

    private async Task RefreshCloudBackupsWithFeedbackAsync()
    {
        try
        {
            await RefreshCloudBackupsAsync();
            _statusLabel.Text = "云备份列表已刷新";
        }
        catch (Exception exception)
        {
            ShowError("无法刷新云备份列表", exception);
        }
    }

    private void OpenCurrentCloudBackupFileLocation()
    {
        var path = GetCloudBackupLocalPath(_cloudBackupGrid.CurrentRow);
        if (path is not null)
        {
            OpenFileLocation(path);
        }
    }

    internal static string? GetCloudBackupLocalPath(DataGridViewRow? row)
    {
        return (row?.Tag as ManagedObjectStorageBackup)?.Source.LocalPath;
    }

    private async Task RestoreSelectedCloudBackupsAsync()
    {
        var selected = GetSelectedCloudBackups();
        if (selected.Count == 0)
        {
            return;
        }

        if (selected.Any(item => !IsRestorableCloudBackup(item)))
        {
            MessageBox.Show(
                this,
                "所选备份中包含配置缺失、校验未通过或缺少 SHA-256 的项目，无法安全取回。",
                "CDSI Beacon",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        if (!TryBeginStatefulOperation())
        {
            return;
        }

        try
        {
            await RestoreCloudBackupsCoreAsync(selected);
        }
        finally
        {
            _progressBar.Style = ProgressBarStyle.Blocks;
            SetBusy(false);
        }
    }

    private async Task RestoreCloudBackupsCoreAsync(
        IReadOnlyList<ManagedObjectStorageBackup> selected)
    {
        _progressBar.Style = ProgressBarStyle.Marquee;
        _progressBar.MarqueeAnimationSpeed = 24;
        _progressLabel.Text = "正在准备从云端取回资产";
        _currentPathLabel.Text = string.Empty;
        _statusLabel.Text = "正在准备云端取回";

        var candidates = selected
            .GroupBy(item => item.Source.AssetId)
            .Select(group => new ObjectStorageRestoreCandidate(
                group.Key,
                group.First().Source.OriginalFilename,
                group.Select(item => new ConfiguredObjectStorageRestoreSource(
                    item.Source,
                    item.Profile!,
                    item.HasStoredSecret)).ToArray()))
            .ToArray();
        string? workspacePath;
        try
        {
            workspacePath = (await _workspaceService.GetAsync())?.Path;
        }
        catch (Exception exception)
        {
            ShowError("无法读取 CDSI 工作目录", exception);
            return;
        }

        using var confirmation = new OssRestoreConfirmationForm(
            candidates,
            workspacePath);
        if (confirmation.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        _scanCancellation?.Dispose();
        _restoreSpeedTracker.Reset();
        _scanCancellation = new CancellationTokenSource();
        _progressBar.MarqueeAnimationSpeed = 0;
        _progressBar.Style = ProgressBarStyle.Continuous;
        _progressBar.Minimum = 0;
        _progressBar.Maximum = 1_000;
        _progressBar.Value = 0;
        _statusLabel.Text = "正在从云端取回资产";
        try
        {
            var result = await _objectStorageRestoreService.RestoreAsync(
                confirmation.SelectedRequests,
                confirmation.Destination,
                new Progress<ObjectStorageRestoreProgress>(UpdateRestoreProgress),
                _scanCancellation.Token);
            await RefreshAssetsAsync();
            ShowRestoreResult(result);
        }
        catch (OperationCanceledException)
        {
            _statusLabel.Text = "云端取回已取消";
        }
        catch (Exception exception)
        {
            _statusLabel.Text = "云端取回失败";
            ShowError("云端取回未能完成", exception);
        }
    }

    private async Task DeleteSelectedCloudBackupsAsync()
    {
        var selected = GetSelectedCloudBackups();
        if (selected.Count == 0 || selected.Any(item => !item.IsAvailable))
        {
            return;
        }

        var prompt = selected.Count == 1
            ? $"将永久删除云端备份“{GetCloudFilename(selected[0].Source.Location.ObjectKey)}”。本地文件和项目关系不会删除，此操作无法撤销。"
            : $"将永久删除所选 {selected.Count:N0} 个云端备份。本地文件和项目关系不会删除，此操作无法撤销。";
        await DeleteCloudBackupsAsync(
            selected,
            prompt,
            "删除备份",
            $"已删除 {selected.Count:N0} 个云端备份");
    }

    private async Task DeleteSelectedCloudBackupProjectAsync()
    {
        var project = GetSelectedCloudBackupProject();
        if (project is null ||
            project.Backups.Count == 0 ||
            project.Backups.Any(backup => !backup.IsAvailable))
        {
            return;
        }

        await DeleteCloudBackupsAsync(
            project.Backups,
            CreateCloudBackupProjectDeletionConfirmation(project),
            "删除备份项目",
            $"已删除备份项目“{project.Name}”中的 {project.Backups.Count:N0} 个云端备份");
    }

    internal static string CreateCloudBackupProjectDeletionConfirmation(
        CloudBackupProjectGroup project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return
            $"将永久删除备份项目“{project.Name}”中的 {project.Backups.Count:N0} 个云端备份。" +
            $"{Environment.NewLine}{Environment.NewLine}" +
            $"云提供商：{FormatCloudBackupProjectProviders(project)}" +
            $"{Environment.NewLine}{Environment.NewLine}" +
            "本地项目、项目成员关系和本地文件不会删除。此操作无法撤销。";
    }

    private async Task DeleteCloudBackupsAsync(
        IReadOnlyList<ManagedObjectStorageBackup> backups,
        string prompt,
        string confirmationTitle,
        string successMessage)
    {
        if (MessageBox.Show(
                this,
                prompt,
                confirmationTitle,
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.OK)
        {
            return;
        }

        SetBusy(true, allowCancel: false);
        var failures = new List<string>();
        try
        {
            for (var index = 0; index < backups.Count; index++)
            {
                var backup = backups[index];
                _statusLabel.Text =
                    $"正在删除云端备份 {index + 1:N0}/{backups.Count:N0}";
                try
                {
                    await _objectStorageManagementService.DeleteAsync(
                        backup.Source.Location.Id);
                }
                catch (Exception exception)
                {
                    _runtimeLog.WriteError("删除云端备份失败", exception);
                    failures.Add(
                        $"{GetCloudFilename(backup.Source.Location.ObjectKey)}: {exception.Message}");
                }
            }

            await RefreshAssetsAsync();
            _statusLabel.Text = failures.Count == 0
                ? successMessage
                : $"云端备份删除完成，失败 {failures.Count:N0} 个";
            _runtimeLog.WriteInformation(
                $"云端备份删除结束；选择={backups.Count:N0}；失败={failures.Count:N0}");
            if (failures.Count > 0)
            {
                MessageBox.Show(
                    this,
                    string.Join(Environment.NewLine, failures.Take(8)),
                    "部分云端备份未删除",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }
        catch (Exception exception)
        {
            _statusLabel.Text = "云端备份删除后刷新失败";
            ShowError("无法完成云端备份删除后的刷新", exception);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private IReadOnlyList<ManagedObjectStorageBackup> GetSelectedCloudBackups()
    {
        return _cloudBackupGrid.SelectedRows
            .Cast<DataGridViewRow>()
            .Select(row => row.Tag)
            .OfType<ManagedObjectStorageBackup>()
            .OrderBy(item => item.Source.OriginalFilename, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private void UpdateCloudBackupActionState()
    {
        var selected = GetSelectedCloudBackups();
        _restoreCloudBackupMenuItem.Enabled = !_isBusy &&
            selected.Count > 0 && selected.All(IsRestorableCloudBackup);
        var project = GetSelectedCloudBackupProject();
        _deleteCloudBackupProjectMenuItem.Enabled = !_isBusy &&
            project is not null &&
            project.Backups.All(backup => backup.IsAvailable);
    }

    private static bool IsRestorableCloudBackup(ManagedObjectStorageBackup backup)
    {
        return backup.IsAvailable &&
            backup.Source.Location.Status == StorageVerificationStatus.Healthy &&
            !string.IsNullOrWhiteSpace(backup.Source.Location.Sha256);
    }

    private static string GetCloudFilename(string objectKey)
    {
        var index = objectKey.LastIndexOf('/');
        return index < 0 ? objectKey : objectKey[(index + 1)..];
    }

    private static string FormatCloudBackupProjectNames(
        IReadOnlyList<string> projectNames)
    {
        return projectNames.Count == 0 ? "无" : string.Join("、", projectNames);
    }

    private static string FormatCloudBackupProvider(ObjectStorageProvider? provider)
    {
        return provider switch
        {
            ObjectStorageProvider.AliyunOss => "阿里云 OSS",
            ObjectStorageProvider.QiniuKodo => "七牛云 Kodo",
            ObjectStorageProvider.TencentCos => "腾讯云 COS",
            null => "配置已删除",
            _ => provider.ToString()!
        };
    }

    private static string FormatCloudVerificationStatus(
        StorageVerificationStatus status)
    {
        return status switch
        {
            StorageVerificationStatus.Healthy => "正常",
            StorageVerificationStatus.Missing => "云端缺失",
            StorageVerificationStatus.SizeMismatch => "大小不一致",
            StorageVerificationStatus.ChecksumMismatch => "校验不一致",
            _ => "待校验"
        };
    }

    internal sealed record CloudBackupProjectGroup(
        string Name,
        bool IsUnassigned,
        IReadOnlyList<ManagedObjectStorageBackup> Backups)
    {
        public int AssetCount => Backups
            .Select(backup => backup.Source.AssetId)
            .Distinct()
            .Count();

        public long TotalSizeBytes => Backups.Sum(
            backup => backup.Source.Location.Size);

        public DateTimeOffset LatestBackupAt => Backups.Max(
            backup => backup.Source.Location.CreatedAt);
    }
}
