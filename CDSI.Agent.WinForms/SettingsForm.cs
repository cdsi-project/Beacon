using CDSI.Agent.Application.OpenWeb;
using CDSI.Agent.Application.Git;
using CDSI.Agent.Application.Scanning;
using CDSI.Agent.Application.Storage;
using CDSI.Agent.Application.Workspaces;
using CDSI.Agent.Core.Assets;
using CDSI.Agent.Core.Scanning;
using CDSI.Agent.Core.Storage;

namespace CDSI.Agent.WinForms;

public sealed partial class SettingsForm : Form
{
    private readonly WorkspaceApplicationService _workspaceService;
    private readonly ScanRootManagementService _scanRootService;
    private readonly ObjectStorageProfileService _storageService;
    private readonly TextBox _workspacePathTextBox = new();
    private readonly DataGridView _rootsGrid = new();
    private readonly ContextMenuStrip _rootContextMenu = new();
    private readonly ToolStripMenuItem _editRootMenuItem = new();
    private readonly ToolStripMenuItem _toggleRootMenuItem = new();
    private readonly ToolStripMenuItem _removeRootMenuItem = new();
    private readonly Label _workspaceStatusLabel = new();
    private readonly DataGridView _storageGrid = new();
    private readonly ContextMenuStrip _storageContextMenu = new();
    private readonly ToolStripMenuItem _editStorageMenuItem = new();
    private readonly ToolStripMenuItem _copyStorageEndpointMenuItem = new();
    private readonly ToolStripMenuItem _copyStorageBucketMenuItem = new();
    private readonly ToolStripMenuItem _deleteStorageMenuItem = new();
    private readonly Button _startScanButton = new();
    private readonly HashSet<Guid> _initialScanRootIds = [];

    internal IReadOnlyCollection<Guid> InitialScanRootIds =>
        _initialScanRootIds.ToArray();

    public SettingsForm(
        WorkspaceApplicationService workspaceService,
        ScanRootManagementService scanRootService,
        ObjectStorageProfileService storageService,
        OpenWebSettingsService openWebSettingsService,
        GitProfileService gitProfileService)
    {
        _workspaceService = workspaceService;
        _scanRootService = scanRootService;
        _storageService = storageService;
        _openWebSettingsService = openWebSettingsService;
        _gitProfileService = gitProfileService;

        Text = "CDSI Beacon 设置";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(820, 520);
        MinimumSize = new Size(720, 460);
        ShowInTaskbar = false;
        Font = new Font("Segoe UI", 9F);
        AutoScaleMode = AutoScaleMode.Dpi;

        var tabs = new TabControl
        {
            Dock = DockStyle.Fill,
            Padding = new Point(12, 5)
        };
        tabs.TabPages.Add(CreateWorkspacePage());
        tabs.TabPages.Add(CreateScanRootsPage());
        tabs.TabPages.Add(CreateStoragePage());
        tabs.TabPages.Add(CreateOpenWebPage());
        tabs.TabPages.Add(CreateGitPage());

        _startScanButton.Text = "开始扫描";
        _startScanButton.BackColor = Color.FromArgb(24, 121, 78);
        _startScanButton.ForeColor = Color.White;
        _startScanButton.FlatStyle = FlatStyle.Flat;
        _startScanButton.FlatAppearance.BorderSize = 0;
        _startScanButton.Cursor = Cursors.Hand;
        _startScanButton.DialogResult = DialogResult.OK;
        _startScanButton.Enabled = false;
        _startScanButton.Size = new Size(104, 32);

        var closeButton = CreateButton(
            "关闭",
            Color.FromArgb(236, 239, 242),
            Color.FromArgb(31, 37, 43));
        closeButton.DialogResult = DialogResult.Cancel;
        closeButton.Anchor = AnchorStyles.Right;
        closeButton.Size = new Size(96, 32);

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 52,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(16, 10, 16, 10),
            BackColor = Color.White
        };
        footer.Controls.Add(_startScanButton);
        footer.Controls.Add(closeButton);

        Controls.Add(tabs);
        Controls.Add(footer);
        Shown += SettingsForm_Shown;
    }

    private TabPage CreateWorkspacePage()
    {
        var page = new TabPage("工作目录")
        {
            BackColor = Color.White,
            Padding = new Padding(24)
        };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 150,
            ColumnCount = 3,
            RowCount = 3
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));

        var label = new Label
        {
            Text = "受管工作目录",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Semibold", 10F),
            ForeColor = Color.FromArgb(31, 37, 43)
        };
        layout.Controls.Add(label, 0, 0);
        layout.SetColumnSpan(label, 3);

        _workspacePathTextBox.Dock = DockStyle.Fill;
        _workspacePathTextBox.Margin = new Padding(0, 5, 8, 5);
        _workspacePathTextBox.AccessibleName = "受管工作目录路径";
        layout.Controls.Add(_workspacePathTextBox, 0, 1);

        var browseButton = CreateButton(
            "浏览",
            Color.FromArgb(236, 239, 242),
            Color.FromArgb(31, 37, 43));
        browseButton.Margin = new Padding(4, 5, 4, 5);
        browseButton.Click += WorkspaceBrowseButton_Click;
        layout.Controls.Add(browseButton, 1, 1);

        var saveButton = CreateButton(
            "应用",
            Color.FromArgb(24, 121, 78),
            Color.White);
        saveButton.Margin = new Padding(4, 5, 0, 5);
        saveButton.Click += WorkspaceSaveButton_Click;
        layout.Controls.Add(saveButton, 2, 1);

        _workspaceStatusLabel.Dock = DockStyle.Fill;
        _workspaceStatusLabel.ForeColor = Color.FromArgb(88, 98, 106);
        _workspaceStatusLabel.Padding = new Padding(0, 8, 0, 0);
        layout.Controls.Add(_workspaceStatusLabel, 0, 2);
        layout.SetColumnSpan(_workspaceStatusLabel, 3);

        page.Controls.Add(layout);
        return page;
    }

    private TabPage CreateScanRootsPage()
    {
        var page = new TabPage("扫描目录")
        {
            BackColor = Color.White,
            Padding = new Padding(16)
        };
        ConfigureRootsGrid();

        var addButton = CreateButton(
            "添加目录",
            Color.FromArgb(24, 121, 78),
            Color.White);
        addButton.Click += AddRootButton_Click;
        addButton.Size = new Size(104, 32);

        var commands = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 46,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(0, 4, 0, 8)
        };
        commands.Controls.Add(addButton);

        page.Controls.Add(_rootsGrid);
        page.Controls.Add(commands);
        return page;
    }

    private TabPage CreateStoragePage()
    {
        var page = new TabPage("备份配置")
        {
            BackColor = Color.White,
            Padding = new Padding(16)
        };
        ConfigureStorageGrid();

        var addButton = CreateButton(
            "添加配置",
            Color.FromArgb(24, 121, 78),
            Color.White);
        addButton.Click += AddStorageButton_Click;
        addButton.Size = new Size(104, 32);

        var commands = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 46,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(0, 4, 0, 8)
        };
        commands.Controls.Add(addButton);

        page.Controls.Add(_storageGrid);
        page.Controls.Add(commands);
        return page;
    }

    private void ConfigureStorageGrid()
    {
        _storageGrid.Dock = DockStyle.Fill;
        _storageGrid.BackgroundColor = Color.White;
        _storageGrid.BorderStyle = BorderStyle.FixedSingle;
        _storageGrid.ReadOnly = true;
        _storageGrid.AllowUserToAddRows = false;
        _storageGrid.AllowUserToDeleteRows = false;
        _storageGrid.AllowUserToResizeRows = false;
        _storageGrid.AutoGenerateColumns = false;
        _storageGrid.MultiSelect = false;
        _storageGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _storageGrid.RowHeadersVisible = false;
        _storageGrid.RowTemplate.Height = 30;
        _storageGrid.ColumnHeadersHeight = 36;
        _storageGrid.AccessibleName = "备份配置列表";
        _storageGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "提供商",
            Width = 120
        });
        _storageGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "名称",
            Width = 130
        });
        _storageGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Endpoint",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            MinimumWidth = 220,
            FillWeight = 100
        });
        _storageGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Bucket",
            Width = 170
        });
        _storageGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "地域",
            Width = 120
        });
        _storageGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "凭据",
            Width = 90
        });
        _editStorageMenuItem.Text = "编辑配置";
        _editStorageMenuItem.Click += EditStorageMenuItem_Click;
        _copyStorageEndpointMenuItem.Text = "复制 Endpoint";
        _copyStorageEndpointMenuItem.Click += (_, _) =>
            CopySelectedStorageValue(copyEndpoint: true);
        _copyStorageBucketMenuItem.Text = "复制 Bucket 名称";
        _copyStorageBucketMenuItem.Click += (_, _) =>
            CopySelectedStorageValue(copyEndpoint: false);
        _deleteStorageMenuItem.Text = "删除配置";
        _deleteStorageMenuItem.ForeColor = Color.FromArgb(137, 49, 49);
        _deleteStorageMenuItem.Click += DeleteStorageMenuItem_Click;
        _storageContextMenu.Items.AddRange(
        [
            _editStorageMenuItem,
            new ToolStripSeparator(),
            _copyStorageEndpointMenuItem,
            _copyStorageBucketMenuItem,
            new ToolStripSeparator(),
            _deleteStorageMenuItem
        ]);
        _storageContextMenu.Opening += (_, args) =>
            args.Cancel =
                _storageGrid.CurrentRow?.Tag is not ConfiguredObjectStorageProfile;
        _storageGrid.ContextMenuStrip = _storageContextMenu;
        _storageGrid.CellMouseDown += (_, args) =>
        {
            if (args.Button == MouseButtons.Right &&
                args.RowIndex >= 0 &&
                args.ColumnIndex >= 0)
            {
                _storageGrid.CurrentCell =
                    _storageGrid.Rows[args.RowIndex].Cells[args.ColumnIndex];
            }
        };
        _storageGrid.CellDoubleClick += StorageGrid_CellDoubleClick;
    }
    private void ConfigureRootsGrid()
    {
        _rootsGrid.Dock = DockStyle.Fill;
        _rootsGrid.BackgroundColor = Color.White;
        _rootsGrid.BorderStyle = BorderStyle.FixedSingle;
        _rootsGrid.ReadOnly = true;
        _rootsGrid.AllowUserToAddRows = false;
        _rootsGrid.AllowUserToDeleteRows = false;
        _rootsGrid.AllowUserToResizeRows = false;
        _rootsGrid.AutoGenerateColumns = false;
        _rootsGrid.MultiSelect = false;
        _rootsGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _rootsGrid.RowHeadersVisible = false;
        _rootsGrid.RowTemplate.Height = 30;
        _rootsGrid.AccessibleName = "外部扫描目录列表";
        _rootsGrid.ColumnHeadersHeight = 36;
        _rootsGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "目录",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            MinimumWidth = 280,
            FillWeight = 100
        });
        _rootsGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "扫描策略",
            Width = 140
        });
        _rootsGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "空闲扫描",
            Width = 120
        });
        _rootsGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "状态",
            Width = 80
        });
        _rootsGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "最近扫描",
            Width = 135
        });
        _editRootMenuItem.Text = "编辑扫描设置";
        _editRootMenuItem.Click += EditRootMenuItem_Click;
        _toggleRootMenuItem.Text = "停用";
        _toggleRootMenuItem.Click += ToggleRootMenuItem_Click;
        _removeRootMenuItem.Text = "移除";
        _removeRootMenuItem.ForeColor = Color.FromArgb(137, 49, 49);
        _removeRootMenuItem.Click += RemoveRootMenuItem_Click;
        _rootContextMenu.Items.AddRange(
        [
            _editRootMenuItem,
            _toggleRootMenuItem,
            new ToolStripSeparator(),
            _removeRootMenuItem
        ]);
        _rootContextMenu.Opening += (_, args) =>
        {
            if (_rootsGrid.CurrentRow?.Tag is not ScanRoot root)
            {
                args.Cancel = true;
                return;
            }

            _toggleRootMenuItem.Text = root.Enabled ? "停用" : "启用";
        };
        _rootsGrid.ContextMenuStrip = _rootContextMenu;
        _rootsGrid.CellMouseDown += (_, args) =>
        {
            if (args.Button == MouseButtons.Right &&
                args.RowIndex >= 0 &&
                args.ColumnIndex >= 0)
            {
                _rootsGrid.CurrentCell =
                    _rootsGrid.Rows[args.RowIndex].Cells[args.ColumnIndex];
            }
        };
        _rootsGrid.CellDoubleClick += RootsGrid_CellDoubleClick;
    }

    private async void SettingsForm_Shown(object? sender, EventArgs e)
    {
        try
        {
            await RefreshWorkspaceAsync();
            await RefreshRootsAsync();
            await RefreshStorageAsync();
            await RefreshOpenWebAsync();
            await RefreshGitProfilesAsync();
        }
        catch (Exception exception)
        {
            ShowError("无法读取设置", exception);
        }
    }

    private async Task RefreshWorkspaceAsync()
    {
        var workspace = await _workspaceService.GetAsync();
        _workspacePathTextBox.Text =
            workspace?.Path ?? WorkspaceApplicationService.GetSuggestedDefaultPath();
        _workspaceStatusLabel.Text = workspace is null
            ? "尚未配置"
            : FormatWorkspaceStatus(workspace.Path, workspace.InboxPath);
    }

    private async Task RefreshRootsAsync()
    {
        var roots = await _scanRootService.ListExternalAsync();
        foreach (var root in roots.Where(root =>
                     root.Enabled && root.LastScannedAt is null))
        {
            _initialScanRootIds.Add(root.Id);
        }

        _rootsGrid.Rows.Clear();
        foreach (var root in roots)
        {
            var index = _rootsGrid.Rows.Add(
                root.Path,
                FormatFileFilter(root),
                FormatIdleScanSchedule(root.GetIdleScanSchedule()),
                FormatRootStatus(root),
                root.LastScannedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "尚未扫描");
            _rootsGrid.Rows[index].Tag = root;
        }

        UpdateStartScanButton();
    }

    private void WorkspaceBrowseButton_Click(object? sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "选择 CDSI 工作目录",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
            InitialDirectory = Directory.Exists(_workspacePathTextBox.Text)
                ? _workspacePathTextBox.Text
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _workspacePathTextBox.Text = dialog.SelectedPath;
        }
    }

    private async void WorkspaceSaveButton_Click(object? sender, EventArgs e)
    {
        try
        {
            var path = _workspacePathTextBox.Text.Trim();
            var current = await _workspaceService.GetAsync();
            if (current is not null &&
                !string.Equals(
                    Path.GetFullPath(current.Path),
                    Path.GetFullPath(path),
                    StringComparison.OrdinalIgnoreCase) &&
                MessageBox.Show(
                    this,
                    "切换后不会搬移或删除旧工作目录中的文件。继续吗？",
                    "更改工作目录",
                    MessageBoxButtons.OKCancel,
                    MessageBoxIcon.Warning) != DialogResult.OK)
            {
                return;
            }

            var result = await _workspaceService.ConfigureAsync(path);
            _workspacePathTextBox.Text = result.Workspace.Path;
            _workspaceStatusLabel.Text = FormatWorkspaceStatus(
                result.Workspace.Path,
                result.Layout.InboxPath);
        }
        catch (Exception exception)
        {
            ShowError("无法设置工作目录", exception);
        }
    }

    private static string FormatWorkspaceStatus(string workspacePath, string inboxPath)
    {
        return $"Inbox: {inboxPath}{Environment.NewLine}" +
            $"数据库备份: {Path.Combine(workspacePath, "System", "DatabaseBackups")}";
    }

    private async void AddRootButton_Click(object? sender, EventArgs e)
    {
        using var dialog = new ScanRootDialog();
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            var result = await _scanRootService.AddExternalAsync(
                dialog.SelectedPath,
                dialog.FileTypeFilters,
                dialog.ExtensionWhitelist);
            await _scanRootService.SetIdleScanScheduleAsync(
                result.Root.Id,
                dialog.IdleScanSchedule);
            if (result.RequiresInitialScan)
            {
                _initialScanRootIds.Add(result.Root.Id);
            }

            await RefreshRootsAsync();
            UpdateStartScanButton();
            if (result.Warnings.Count > 0)
            {
                MessageBox.Show(
                    this,
                    string.Join(Environment.NewLine, result.Warnings),
                    "目录重叠",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }
        catch (Exception exception)
        {
            ShowError("无法添加扫描目录", exception);
        }
    }

    private async void EditRootMenuItem_Click(object? sender, EventArgs e)
    {
        await EditSelectedRootAsync();
    }

    private async void RootsGrid_CellDoubleClick(
        object? sender,
        DataGridViewCellEventArgs e)
    {
        if (e.RowIndex >= 0)
        {
            await EditSelectedRootAsync();
        }
    }

    private async Task EditSelectedRootAsync()
    {
        if (_rootsGrid.CurrentRow?.Tag is not ScanRoot root)
        {
            return;
        }

        using var dialog = new ScanRootDialog(
            root.Path,
            root.CreateFileFilter().FileTypeFilters,
            root.CreateFileFilter().ExtensionWhitelist,
            allowPathSelection: false,
            idleScanSchedule: root.GetIdleScanSchedule());
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            await _scanRootService.SetFileFilterAsync(
                root.Id,
                dialog.FileTypeFilters,
                dialog.ExtensionWhitelist);
            await _scanRootService.SetIdleScanScheduleAsync(
                root.Id,
                dialog.IdleScanSchedule);
            await RefreshRootsAsync();
        }
        catch (Exception exception)
        {
            ShowError("无法更新扫描设置", exception);
        }
    }

    private async void ToggleRootMenuItem_Click(object? sender, EventArgs e)
    {
        if (_rootsGrid.CurrentRow?.Tag is not ScanRoot root)
        {
            return;
        }

        try
        {
            var enable = !root.Enabled;
            await _scanRootService.SetEnabledAsync(root.Id, enable);
            if (!enable)
            {
                _initialScanRootIds.Remove(root.Id);
            }
            else if (root.LastScannedAt is null)
            {
                _initialScanRootIds.Add(root.Id);
            }

            await RefreshRootsAsync();
            UpdateStartScanButton();
        }
        catch (Exception exception)
        {
            ShowError("无法更新扫描目录", exception);
        }
    }

    private async void RemoveRootMenuItem_Click(object? sender, EventArgs e)
    {
        if (_rootsGrid.CurrentRow?.Tag is not ScanRoot root ||
            MessageBox.Show(
                this,
                "移除后停止扫描此目录，已有资产和位置记录会保留。",
                "移除扫描目录",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Warning) != DialogResult.OK)
        {
            return;
        }

        try
        {
            await _scanRootService.RemoveAsync(root.Id);
            _initialScanRootIds.Remove(root.Id);
            await RefreshRootsAsync();
            UpdateStartScanButton();
        }
        catch (Exception exception)
        {
            ShowError("无法移除扫描目录", exception);
        }
    }

    private void UpdateStartScanButton()
    {
        _startScanButton.Enabled = _initialScanRootIds.Count > 0;
    }

    internal static string FormatFileTypeFilter(AssetFileTypeFilter fileTypeFilter)
    {
        return fileTypeFilter switch
        {
            AssetFileTypeFilter.All => "全部类型",
            AssetFileTypeFilter.Video => "视频",
            AssetFileTypeFilter.Audio => "音频",
            AssetFileTypeFilter.Image => "图片",
            AssetFileTypeFilter.Document => "文档",
            AssetFileTypeFilter.Other => "其他",
            _ => throw new ArgumentOutOfRangeException(nameof(fileTypeFilter))
        };
    }

    internal static string FormatFileFilter(ScanRoot root)
    {
        var filter = root.CreateFileFilter();
        var parts = new List<string>();
        if (filter.FileTypeFilters.Count == ScanFileFilter.AllFileTypes.Count)
        {
            parts.Add("全部类型");
        }
        else if (filter.FileTypeFilters.Count > 0)
        {
            parts.Add(string.Join("、", filter.FileTypeFilters.Select(
                FormatFileTypeFilter)));
        }

        if (filter.ExtensionWhitelist.Count > 0)
        {
            var preview = string.Join(", ", filter.ExtensionWhitelist.Take(3));
            parts.Add(filter.ExtensionWhitelist.Count <= 3
                ? $"扩展名: {preview}"
                : $"扩展名: {preview} 等 {filter.ExtensionWhitelist.Count} 种");
        }

        return string.Join("；", parts);
    }

    internal static string FormatIdleScanSchedule(IdleScanSchedule schedule)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        if (!schedule.Enabled)
        {
            return "关闭";
        }

        var unit = schedule.Unit switch
        {
            IdleScanIntervalUnit.Minutes => "分钟",
            IdleScanIntervalUnit.Hours => "小时",
            IdleScanIntervalUnit.Days => "天",
            _ => throw new ArgumentOutOfRangeException(nameof(schedule))
        };
        return $"每 {schedule.Interval} {unit}";
    }

    private static string FormatRootStatus(ScanRoot root)
    {
        if (!root.Enabled)
        {
            return "已停用";
        }

        return root.Status switch
        {
            ScanRootStatus.Active => "正常",
            ScanRootStatus.Unavailable => "不可用",
            ScanRootStatus.Offline => "设备离线",
            ScanRootStatus.Error => "有错误",
            _ => root.Status.ToString()
        };
    }

    private async Task RefreshStorageAsync()
    {
        var profiles = await _storageService.ListAsync();
        _storageGrid.Rows.Clear();
        foreach (var configured in profiles)
        {
            var profile = configured.Profile;
            var index = _storageGrid.Rows.Add(
                FormatStorageProvider(profile.Provider),
                profile.DisplayName,
                $"{(profile.UseHttps ? "https" : "http")}://{profile.Endpoint}",
                profile.BucketName,
                profile.Region ?? string.Empty,
                configured.HasStoredSecret ? "已保存" : "缺失");
            _storageGrid.Rows[index].Tag = configured;
        }
    }

    private async void AddStorageButton_Click(object? sender, EventArgs e)
    {
        await ShowStorageDialogAsync(null);
    }

    private async void EditStorageMenuItem_Click(object? sender, EventArgs e)
    {
        await EditSelectedStorageAsync();
    }

    private async void StorageGrid_CellDoubleClick(
        object? sender,
        DataGridViewCellEventArgs e)
    {
        if (e.RowIndex >= 0)
        {
            await EditSelectedStorageAsync();
        }
    }

    private async Task EditSelectedStorageAsync()
    {
        if (_storageGrid.CurrentRow?.Tag is ConfiguredObjectStorageProfile configured)
        {
            await ShowStorageDialogAsync(configured.Profile);
        }
    }

    private async Task ShowStorageDialogAsync(
        CDSI.Agent.Core.Storage.ObjectStorageProfile? profile)
    {
        using var dialog = new OssProfileDialog(profile);
        while (dialog.ShowDialog(this) == DialogResult.OK)
        {
            try
            {
                await _storageService.SaveAsync(dialog.CreateRequest());
                await RefreshStorageAsync();
                return;
            }
            catch (Exception exception)
            {
                ShowError("无法保存备份配置", exception);
                dialog.DialogResult = DialogResult.None;
            }
        }
    }

    private void CopySelectedStorageValue(bool copyEndpoint)
    {
        if (_storageGrid.CurrentRow?.Tag is not ConfiguredObjectStorageProfile configured)
        {
            return;
        }

        try
        {
            var profile = configured.Profile;
            Clipboard.SetText(copyEndpoint
                ? $"{(profile.UseHttps ? "https" : "http")}://{profile.Endpoint}"
                : profile.BucketName);
        }
        catch (Exception exception)
        {
            ShowError("无法复制备份配置信息", exception);
        }
    }

    private async void DeleteStorageMenuItem_Click(object? sender, EventArgs e)
    {
        if (_storageGrid.CurrentRow?.Tag is not ConfiguredObjectStorageProfile configured ||
            MessageBox.Show(
                this,
                "将删除本机配置和 Windows 凭据，不会删除 Bucket 或其中的对象。",
                "删除备份配置",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Warning) != DialogResult.OK)
        {
            return;
        }

        try
        {
            await _storageService.DeleteAsync(configured.Profile.Id);
            await RefreshStorageAsync();
        }
        catch (Exception exception)
        {
            ShowError("无法删除备份配置", exception);
        }
    }

    private static string FormatStorageProvider(ObjectStorageProvider provider)
    {
        return provider switch
        {
            ObjectStorageProvider.AliyunOss => "阿里云 OSS",
            ObjectStorageProvider.QiniuKodo => "七牛云 Kodo",
            ObjectStorageProvider.TencentCos => "腾讯云 COS",
            _ => provider.ToString()
        };
    }

    private static Button CreateButton(string text, Color background, Color foreground)
    {
        return new Button
        {
            Text = text,
            BackColor = background,
            ForeColor = foreground,
            FlatStyle = FlatStyle.Flat,
            FlatAppearance = { BorderSize = 0 },
            Cursor = Cursors.Hand
        };
    }

    private void ShowError(string title, Exception exception)
    {
        MessageBox.Show(
            this,
            exception.Message,
            title,
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }
}
