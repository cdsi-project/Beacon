using System.Diagnostics;
using CDSI.Agent.Application.Git;
using CDSI.Agent.Core.Collections;
using CDSI.Agent.Core.Git;

namespace CDSI.Agent.WinForms;

public sealed partial class MainForm
{
    private readonly TabPage _gitProjectsTabPage = new("Git项目管理");
    private readonly DataGridView _gitProjectGrid = new();
    private readonly TextBox _gitProjectSearchTextBox = new();
    private readonly Button _searchGitProjectsButton = new();
    private readonly Button _resetGitProjectSearchButton = new();
    private readonly Label _gitProjectSummaryLabel = new();
    private readonly ContextMenuStrip _gitProjectContextMenu = new();
    private readonly ToolStripMenuItem _syncGitProjectMenuItem = new();
    private readonly ToolStripMenuItem _openGitProjectMenuItem = new();
    private readonly ToolStripMenuItem _openGitRepositoryMenuItem = new();
    private readonly ToolStripMenuItem _copyGitRepositoryUrlMenuItem = new();
    private IReadOnlyList<GitProjectSyncRecord> _availableGitProjectSyncs = [];

    private void ConfigureGitProjectManagementTab()
    {
        ConfigureGrid(_gitProjectGrid);
        _gitProjectGrid.AccessibleName = "Git项目列表";
        ConfigureGitProjectGridColumns(_gitProjectGrid);
        _gitProjectGrid.CellDoubleClick += async (_, args) =>
        {
            if (args.RowIndex >= 0)
            {
                await OpenSelectedGitProjectAsync();
            }
        };
        _gitProjectGrid.MouseDown += (_, args) =>
        {
            if (args.Button != MouseButtons.Right)
            {
                return;
            }

            var hit = _gitProjectGrid.HitTest(args.X, args.Y);
            if (hit.RowIndex >= 0)
            {
                ApplyAssetGridRightClickSelection(
                    _gitProjectGrid,
                    hit.RowIndex,
                    hit.ColumnIndex,
                    Keys.None);
            }
        };

        _gitProjectSearchTextBox.Width = 280;
        _gitProjectSearchTextBox.Margin = new Padding(0, 3, 0, 0);
        _gitProjectSearchTextBox.PlaceholderText = "搜索项目、仓库、分支或提交";
        _gitProjectSearchTextBox.AccessibleName = "Git项目搜索";
        _gitProjectSearchTextBox.KeyDown += (_, eventArgs) =>
        {
            if (eventArgs.KeyCode != Keys.Enter)
            {
                return;
            }

            eventArgs.SuppressKeyPress = true;
            ApplyGitProjectSearchWithFeedback();
        };
        ConfigureFilterButton(
            _searchGitProjectsButton,
            "搜索",
            Color.FromArgb(24, 121, 78),
            Color.White);
        ConfigureCollectionActionButton(
            _resetGitProjectSearchButton,
            "重置",
            Color.FromArgb(236, 239, 242),
            Color.FromArgb(31, 37, 43));
        _resetGitProjectSearchButton.Width = 88;
        _searchGitProjectsButton.Click += (_, _) =>
            ApplyGitProjectSearchWithFeedback();
        _resetGitProjectSearchButton.Click += (_, _) =>
        {
            _gitProjectSearchTextBox.Clear();
            ApplyGitProjectSearchWithFeedback();
        };

        _gitProjectSummaryLabel.AutoSize = true;
        _gitProjectSummaryLabel.Margin = new Padding(8, 8, 0, 0);
        _gitProjectSummaryLabel.ForeColor = Color.FromArgb(88, 98, 106);
        _gitProjectSummaryLabel.AccessibleName = "Git项目统计";

        ConfigureGitProjectContextMenu(
            _gitProjectContextMenu,
            _syncGitProjectMenuItem,
            _openGitProjectMenuItem,
            _openGitRepositoryMenuItem,
            _copyGitRepositoryUrlMenuItem);
        _syncGitProjectMenuItem.Click += async (_, _) =>
            await SyncSelectedGitProjectAsync();
        _openGitProjectMenuItem.Click += async (_, _) =>
            await OpenSelectedGitProjectAsync();
        _openGitRepositoryMenuItem.Click += (_, _) =>
            OpenSelectedGitRepository();
        _copyGitRepositoryUrlMenuItem.Click += (_, _) =>
            CopySelectedGitRepositoryUrl();
        _gitProjectContextMenu.Opening += (_, args) =>
        {
            var item = GetSelectedGitProjectItem();
            args.Cancel = item is null;
            _syncGitProjectMenuItem.Enabled = !_isBusy &&
                item?.LocalProjectAvailable == true &&
                item.GitProfileAvailable;
            _openGitProjectMenuItem.Enabled = !_isBusy &&
                item?.LocalProjectAvailable == true;
            _openGitRepositoryMenuItem.Enabled = !_isBusy &&
                item is not null &&
                TryCreateGitRepositoryBrowserUrl(
                    item.Record,
                    out var repositoryBrowserUrl) &&
                repositoryBrowserUrl.Length > 0;
            _copyGitRepositoryUrlMenuItem.Enabled = !_isBusy && item is not null;
        };
        _gitProjectGrid.ContextMenuStrip = _gitProjectContextMenu;

        _gitProjectsTabPage.Padding = Padding.Empty;
        _gitProjectsTabPage.BackColor = Color.White;
        _gitProjectsTabPage.Controls.Add(CreateGitProjectManagementLayout(
            _gitProjectSearchTextBox,
            _searchGitProjectsButton,
            _resetGitProjectSearchButton,
            _gitProjectSummaryLabel,
            _gitProjectGrid));
    }

    internal static void ConfigureGitProjectGridColumns(DataGridView grid)
    {
        ArgumentNullException.ThrowIfNull(grid);
        grid.Columns.Add(CreateColumn(
            "项目",
            180,
            DataGridViewAutoSizeColumnMode.Fill,
            28,
            minimumWidth: 130));
        grid.Columns.Add(CreateColumn("类型", 72));
        grid.Columns.Add(CreateColumn(
            "Git仓库",
            160,
            DataGridViewAutoSizeColumnMode.Fill,
            24,
            minimumWidth: 120));
        grid.Columns.Add(CreateColumn("平台", 90));
        grid.Columns.Add(CreateColumn(
            "仓库地址",
            260,
            DataGridViewAutoSizeColumnMode.Fill,
            36,
            minimumWidth: 180));
        grid.Columns.Add(CreateColumn("分支", 90));
        grid.Columns.Add(CreateColumn("最近提交", 112));
        grid.Columns.Add(CreateColumn("资产", 64));
        var sizeColumn = CreateFileSizeColumn();
        sizeColumn.HeaderText = "大小";
        grid.Columns.Add(sizeColumn);
        grid.Columns.Add(CreateColumn("本地状态", 120));
        grid.Columns.Add(CreateColumn("同步时间", 145));
        EnableFreeColumnResizing(grid);
    }

    internal static void ConfigureGitProjectContextMenu(
        ContextMenuStrip contextMenu,
        ToolStripMenuItem syncItem,
        ToolStripMenuItem openProjectItem,
        ToolStripMenuItem openRepositoryItem,
        ToolStripMenuItem copyRepositoryUrlItem)
    {
        ArgumentNullException.ThrowIfNull(contextMenu);
        ArgumentNullException.ThrowIfNull(syncItem);
        ArgumentNullException.ThrowIfNull(openProjectItem);
        ArgumentNullException.ThrowIfNull(openRepositoryItem);
        ArgumentNullException.ThrowIfNull(copyRepositoryUrlItem);
        syncItem.Text = "同步到Git";
        openProjectItem.Text = "打开所在项目";
        openRepositoryItem.Text = "打开仓库";
        copyRepositoryUrlItem.Text = "复制仓库地址";
        contextMenu.Items.Clear();
        contextMenu.Items.AddRange(
        [
            syncItem,
            new ToolStripSeparator(),
            openProjectItem,
            openRepositoryItem,
            copyRepositoryUrlItem
        ]);
    }

    internal static Control CreateGitProjectManagementLayout(
        Control searchTextBox,
        Control searchButton,
        Control resetButton,
        Control summaryLabel,
        DataGridView grid)
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
        toolbar.Controls.Add(searchTextBox);
        toolbar.Controls.Add(searchButton);
        toolbar.Controls.Add(resetButton);
        toolbar.Controls.Add(summaryLabel);
        layout.Controls.Add(toolbar, 0, 0);
        layout.Controls.Add(grid, 0, 1);
        return layout;
    }

    private async Task RefreshGitProjectsAsync()
    {
        _availableGitProjectSyncs = await _gitProjectSyncService.ListAsync();
        ApplyGitProjectSearch();
    }

    private void ApplyGitProjectSearchWithFeedback()
    {
        var visibleCount = ApplyGitProjectSearch();
        _statusLabel.Text = string.IsNullOrWhiteSpace(_gitProjectSearchTextBox.Text)
            ? "已显示全部 Git 项目"
            : $"Git 项目搜索完成，找到 {visibleCount:N0} 个结果";
    }

    private int ApplyGitProjectSearch()
    {
        var selected = GetSelectedGitProjectItem()?.Record;
        var items = CreateGitProjectManagementItems(
            _availableGitProjectSyncs,
            _availableCollections,
            _availableGitProfiles);
        var filtered = FilterGitProjectManagementItems(
            items,
            _gitProjectSearchTextBox.Text);
        _gitProjectGrid.Rows.Clear();
        foreach (var item in filtered)
        {
            var record = item.Record;
            var rowIndex = _gitProjectGrid.Rows.Add(
                item.ProjectName,
                FormatCollectionType(item.ProjectType),
                record.ProfileName,
                FormatGitProjectProvider(record.Provider),
                record.RepositoryUrl,
                record.Branch,
                FormatGitCommit(record.CommitId),
                record.SyncedFiles,
                record.SyncedBytes,
                item.LocalState,
                record.SyncedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
            _gitProjectGrid.Rows[rowIndex].Tag = item;
            _gitProjectGrid.Rows[rowIndex].Cells[4].ToolTipText = record.RepositoryUrl;
            _gitProjectGrid.Rows[rowIndex].Cells[6].ToolTipText = record.CommitId;
        }

        var rowToSelect = _gitProjectGrid.Rows
            .Cast<DataGridViewRow>()
            .FirstOrDefault(row =>
                row.Tag is GitProjectManagementItem item &&
                selected is not null &&
                item.Record.ProjectId == selected.ProjectId &&
                item.Record.ProfileId == selected.ProfileId)
            ?? _gitProjectGrid.Rows.Cast<DataGridViewRow>().FirstOrDefault();
        if (rowToSelect is not null)
        {
            _gitProjectGrid.CurrentCell = rowToSelect.Cells[0];
            rowToSelect.Selected = true;
        }

        _gitProjectSummaryLabel.Text =
            $"同步记录 {_availableGitProjectSyncs.Count:N0} · 当前显示 {filtered.Count:N0}";
        _gitProjectsTabPage.Text =
            $"Git项目管理 ({_availableGitProjectSyncs.Count:N0})";
        return filtered.Count;
    }

    internal static IReadOnlyList<GitProjectManagementItem> CreateGitProjectManagementItems(
        IReadOnlyList<GitProjectSyncRecord> records,
        IReadOnlyList<AssetCollectionSummary> projects,
        IReadOnlyList<ConfiguredGitProfile> profiles)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(profiles);
        var projectsById = projects.ToDictionary(project => project.Id);
        var profileIds = profiles.Select(profile => profile.Profile.Id).ToHashSet();
        return records.Select(record =>
        {
            var projectAvailable = projectsById.TryGetValue(
                record.ProjectId,
                out var project);
            var profileAvailable = profileIds.Contains(record.ProfileId);
            var localState = (projectAvailable, profileAvailable) switch
            {
                (true, true) => "可用",
                (false, true) => "项目已删除",
                (true, false) => "Git配置已删除",
                _ => "项目、配置已删除"
            };
            return new GitProjectManagementItem(
                record,
                project?.Name ?? record.ProjectName,
                project?.Type ?? record.ProjectType,
                projectAvailable,
                profileAvailable,
                localState);
        }).ToArray();
    }

    internal static IReadOnlyList<GitProjectManagementItem> FilterGitProjectManagementItems(
        IReadOnlyList<GitProjectManagementItem> items,
        string? query)
    {
        ArgumentNullException.ThrowIfNull(items);
        var normalized = query?.Trim();
        if (string.IsNullOrEmpty(normalized))
        {
            return items;
        }

        return items.Where(item =>
        {
            var record = item.Record;
            return item.ProjectName.Contains(normalized, StringComparison.OrdinalIgnoreCase) ||
                record.ProfileName.Contains(normalized, StringComparison.OrdinalIgnoreCase) ||
                record.RepositoryUrl.Contains(normalized, StringComparison.OrdinalIgnoreCase) ||
                record.Branch.Contains(normalized, StringComparison.OrdinalIgnoreCase) ||
                record.CommitId.Contains(normalized, StringComparison.OrdinalIgnoreCase) ||
                FormatGitProjectProvider(record.Provider).Contains(
                    normalized,
                    StringComparison.OrdinalIgnoreCase) ||
                item.LocalState.Contains(normalized, StringComparison.OrdinalIgnoreCase);
        }).ToArray();
    }

    private GitProjectManagementItem? GetSelectedGitProjectItem()
    {
        return _gitProjectGrid.CurrentRow?.Tag as GitProjectManagementItem;
    }

    private async Task SyncSelectedGitProjectAsync()
    {
        var item = GetSelectedGitProjectItem();
        var project = item is null
            ? null
            : _availableCollections.FirstOrDefault(
                project => project.Id == item.Record.ProjectId);
        if (item is null || project is null || !item.GitProfileAvailable)
        {
            return;
        }

        await PrepareAndSyncProjectToGitAsync(project, item.Record.ProfileId);
    }

    private async Task OpenSelectedGitProjectAsync()
    {
        var item = GetSelectedGitProjectItem();
        if (item is null)
        {
            return;
        }

        if (!item.LocalProjectAvailable)
        {
            MessageBox.Show(
                this,
                "该同步记录对应的本地项目已被删除。Git 仓库和本地同步记录不会因此删除。",
                "CDSI Beacon",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        _mainTabControl.SelectedTab = _collectionsTabPage;
        await RefreshAssetCollectionsAsync(item.Record.ProjectId);
    }

    private void OpenSelectedGitRepository()
    {
        var item = GetSelectedGitProjectItem();
        if (item is null ||
            !TryCreateGitRepositoryBrowserUrl(item.Record, out var url))
        {
            return;
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo(url)
            {
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            ShowError("无法打开 Git 仓库", exception);
        }
    }

    private void CopySelectedGitRepositoryUrl()
    {
        var item = GetSelectedGitProjectItem();
        if (item is null)
        {
            return;
        }

        try
        {
            Clipboard.SetText(item.Record.RepositoryUrl);
            _statusLabel.Text = "已复制 Git 仓库地址";
        }
        catch (Exception exception)
        {
            ShowError("无法复制 Git 仓库地址", exception);
        }
    }

    internal static bool TryCreateGitRepositoryBrowserUrl(
        GitProjectSyncRecord record,
        out string url)
    {
        ArgumentNullException.ThrowIfNull(record);
        var expectedHost = record.Provider switch
        {
            GitHostingProvider.GitHub => "github.com",
            GitHostingProvider.Gitee => "gitee.com",
            _ => string.Empty
        };
        if (expectedHost.Length == 0)
        {
            url = string.Empty;
            return false;
        }

        string path;
        if (Uri.TryCreate(record.RepositoryUrl, UriKind.Absolute, out var repositoryUri) &&
            repositoryUri.Scheme == Uri.UriSchemeHttps &&
            string.Equals(
                repositoryUri.Host,
                expectedHost,
                StringComparison.OrdinalIgnoreCase))
        {
            path = repositoryUri.AbsolutePath.Trim('/');
        }
        else
        {
            var prefix = $"git@{expectedHost}:";
            if (!record.RepositoryUrl.StartsWith(
                    prefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                url = string.Empty;
                return false;
            }

            path = record.RepositoryUrl[prefix.Length..];
        }

        path = path.EndsWith(".git", StringComparison.OrdinalIgnoreCase)
            ? path[..^4]
            : path;
        if (path.Length == 0 ||
            path.Contains('\\') ||
            path.Contains("..", StringComparison.Ordinal) ||
            path.Contains('?') ||
            path.Contains('#') ||
            !path.Contains('/'))
        {
            url = string.Empty;
            return false;
        }

        url = $"https://{expectedHost}/{path}";
        return Uri.TryCreate(url, UriKind.Absolute, out _);
    }

    internal static string FormatGitProjectProvider(GitHostingProvider provider)
    {
        return provider switch
        {
            GitHostingProvider.GitHub => "GitHub",
            GitHostingProvider.Gitee => "Gitee（码云）",
            _ => provider.ToString()
        };
    }

    internal static string FormatGitCommit(string commitId)
    {
        return commitId.Length <= 12 ? commitId : commitId[..12];
    }
}

public sealed record GitProjectManagementItem(
    GitProjectSyncRecord Record,
    string ProjectName,
    AssetCollectionType ProjectType,
    bool LocalProjectAvailable,
    bool GitProfileAvailable,
    string LocalState);
