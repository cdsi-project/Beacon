using System.Diagnostics;
using CDSI.Agent.Core.Assets;
using CDSI.Agent.Core.Fingerprints;
using CDSI.Agent.Core.Transfers;

namespace CDSI.Agent.WinForms;

public sealed partial class MainForm
{
    private readonly MenuStrip _mainMenuStrip = new();
    private readonly ToolStripMenuItem _startStandardScanMenuItem = new();
    private readonly ToolStripMenuItem _startFullScanMenuItem = new();
    private readonly ToolStripMenuItem _cancelScanMenuItem = new();
    private readonly ToolStripMenuItem _refreshAssetsMenuItem = new();
    private readonly ToolStripMenuItem _createProjectMenuItem = new();
    private readonly ToolStripMenuItem _fileSettingsMenuItem = new();
    private readonly ToolStripMenuItem _backupDatabaseMenuItem = new();
    private readonly ToolStripMenuItem _openDatabaseBackupDirectoryMenuItem = new();
    private readonly ToolStripMenuItem _settingsMenuItem = new();
    private readonly ToolStripMenuItem _mainAssetMenuItem = new();
    private readonly ToolStripMenuItem _checkForUpdatesMenuItem = new();

    private void ConfigureMainMenu()
    {
        var fileMenu = new ToolStripMenuItem("文件(&F)");
        _createProjectMenuItem.Text = "新建项目(&N)";
        _createProjectMenuItem.ShortcutKeyDisplayString = "Ctrl+N";
        _createProjectMenuItem.Click += async (_, _) =>
        {
            _mainTabControl.SelectedTab = _collectionsTabPage;
            await CreateCollectionAsync();
        };
        var openWorkspaceItem = new ToolStripMenuItem("打开 CDSI 工作目录(&W)");
        openWorkspaceItem.Click += async (_, _) => await OpenWorkspaceDirectoryAsync();
        _fileSettingsMenuItem.Text = "扫描目录设置...";
        _fileSettingsMenuItem.Click += async (_, _) => await OpenSettingsAsync();
        var openDataDirectoryItem = new ToolStripMenuItem("打开数据目录(&D)");
        openDataDirectoryItem.Click += (_, _) => OpenDirectoryPath(
            _dataDirectory,
            "无法打开数据目录");
        _backupDatabaseMenuItem.Text = "立即备份数据库(&B)";
        _backupDatabaseMenuItem.Click += async (_, _) =>
            await CreateDatabaseBackupManuallyAsync();
        _openDatabaseBackupDirectoryMenuItem.Text = "打开数据库备份目录";
        _openDatabaseBackupDirectoryMenuItem.Click += async (_, _) =>
            await OpenDatabaseBackupDirectoryAsync();
        var exitItem = new ToolStripMenuItem("退出(&X)");
        exitItem.Click += (_, _) => Close();
        fileMenu.DropDownItems.AddRange(
            [
                _createProjectMenuItem,
                new ToolStripSeparator(),
                openWorkspaceItem,
                _fileSettingsMenuItem,
                openDataDirectoryItem,
                new ToolStripSeparator(),
                _backupDatabaseMenuItem,
                _openDatabaseBackupDirectoryMenuItem,
                new ToolStripSeparator(),
                exitItem
            ]);
        fileMenu.DropDownOpening += (_, _) => UpdateMainMenuState();

        var scanMenu = new ToolStripMenuItem("扫描(&S)");
        _startStandardScanMenuItem.Text = "常规扫描(&S)";
        _startStandardScanMenuItem.ShortcutKeys = Keys.F6;
        _startStandardScanMenuItem.Click += async (_, _) =>
            await StartConfiguredScanAsync(FingerprintMode.DuplicateCandidates);
        _startFullScanMenuItem.Text = "完整校验扫描(&F)";
        _startFullScanMenuItem.ShortcutKeys = Keys.Control | Keys.F6;
        _startFullScanMenuItem.Click += async (_, _) =>
            await StartConfiguredScanAsync(FingerprintMode.Complete);
        _cancelScanMenuItem.Text = "取消当前任务(&C)";
        ConfigureEscapeShortcutDisplay(_cancelScanMenuItem);
        _cancelScanMenuItem.Click += (_, _) => _scanCancellation?.Cancel();
        _refreshAssetsMenuItem.Text = "刷新资产索引(&R)";
        _refreshAssetsMenuItem.ShortcutKeys = Keys.F5;
        _refreshAssetsMenuItem.Click += async (_, _) => await RefreshAssetPageAsync();
        scanMenu.DropDownItems.AddRange(
            [
                _startStandardScanMenuItem,
                _startFullScanMenuItem,
                _cancelScanMenuItem,
                new ToolStripSeparator(),
                _refreshAssetsMenuItem
            ]);
        scanMenu.DropDownOpening += (_, _) => UpdateMainMenuState();

        ConfigureMainAssetMenu();

        var viewMenu = new ToolStripMenuItem("视图(&V)");
        viewMenu.DropDownItems.AddRange(
            [
                CreateTabMenuItem("全部资产", _assetsTabPage, Keys.Control | Keys.D1),
                CreateTabMenuItem("资产目录", _assetDirectoriesTabPage, Keys.Control | Keys.D2),
                CreateTabMenuItem("重复文件", _duplicatesTabPage, Keys.Control | Keys.D3),
                CreateTabMenuItem("项目管理", _collectionsTabPage, Keys.Control | Keys.D4),
                CreateTabMenuItem("云备份管理", _cloudBackupsTabPage, Keys.Control | Keys.D5),
                CreateTabMenuItem("Git项目管理", _gitProjectsTabPage, Keys.Control | Keys.D6),
                CreateTabMenuItem("RSS订阅", _readerTabPage, Keys.Control | Keys.D7),
                CreateTabMenuItem("统计", _statisticsTabPage, Keys.Control | Keys.D8),
                new ToolStripSeparator(),
                CreateMenuItem("重置资产列表列宽", (_, _) =>
                    ResetGridColumnWidths(_assetGrid))
            ]);
        viewMenu.DropDownOpening += (_, _) =>
        {
            foreach (var item in viewMenu.DropDownItems.OfType<ToolStripMenuItem>())
            {
                item.Checked = item.Tag is TabPage tabPage &&
                    ReferenceEquals(_mainTabControl.SelectedTab, tabPage);
            }
        };

        var toolsMenu = new ToolStripMenuItem("工具(&T)");
        var taskCenterItem = new ToolStripMenuItem("任务中心(&J)")
        {
            ShortcutKeys = Keys.Control | Keys.J
        };
        taskCenterItem.Click += (_, _) => ShowTaskCenter();
        var runtimeLogItem = new ToolStripMenuItem("运行日志(&L)");
        runtimeLogItem.Click += (_, _) => ShowRuntimeLog();
        toolsMenu.DropDownItems.AddRange([taskCenterItem, runtimeLogItem]);
        toolsMenu.DropDownOpening += (_, _) => UpdateMainMenuState();

        _settingsMenuItem.Text = "设置(&O)";
        ConfigureSettingsShortcutDisplay(_settingsMenuItem);
        _settingsMenuItem.Click += async (_, _) => await OpenSettingsAsync();

        var helpMenu = new ToolStripMenuItem("帮助(&H)");
        var readmeItem = new ToolStripMenuItem("使用文档(&D)")
        {
            ShortcutKeys = Keys.F1
        };
        readmeItem.Click += (_, _) => OpenBundledDocument("README.md");
        var safetyItem = new ToolStripMenuItem("数据安全与隐私(&S)");
        safetyItem.Click += (_, _) => ShowDataSafetyInformation();
        var licenseItem = new ToolStripMenuItem("开源协议(&L)");
        licenseItem.Click += (_, _) =>
            ShowLegalDocuments(LegalDocumentPage.OpenSourceLicense);
        var thirdPartyItem = new ToolStripMenuItem("第三方许可(&T)");
        thirdPartyItem.Click += (_, _) =>
            ShowLegalDocuments(LegalDocumentPage.ThirdPartyNotices);
        _checkForUpdatesMenuItem.Text = "检查更新(&U)";
        _checkForUpdatesMenuItem.Click += async (_, _) =>
            await CheckForUpdatesAsync(showCurrentStatus: true);
        var aboutItem = new ToolStripMenuItem("关于 CDSI Beacon(&A)");
        aboutItem.Click += (_, _) => ShowAboutDialog();
        helpMenu.DropDownItems.AddRange(
            [
                readmeItem,
                safetyItem,
                new ToolStripSeparator(),
                licenseItem,
                thirdPartyItem,
                new ToolStripSeparator(),
                _checkForUpdatesMenuItem,
                aboutItem
            ]);

        ConfigureMainMenuStrip(
            _mainMenuStrip,
            [
                fileMenu,
                scanMenu,
                _mainAssetMenuItem,
                viewMenu,
                toolsMenu,
                _settingsMenuItem,
                helpMenu
            ]);
        UpdateMainMenuState();
    }

    internal static void ConfigureMainMenuStrip(
        MenuStrip menuStrip,
        IReadOnlyList<ToolStripMenuItem> topLevelItems)
    {
        ArgumentNullException.ThrowIfNull(menuStrip);
        ArgumentNullException.ThrowIfNull(topLevelItems);
        menuStrip.Items.Clear();
        menuStrip.Items.AddRange(topLevelItems.Cast<ToolStripItem>().ToArray());
        menuStrip.Dock = DockStyle.Fill;
        menuStrip.Padding = new Padding(20, 2, 20, 2);
        menuStrip.BackColor = Color.White;
        menuStrip.ForeColor = Color.FromArgb(31, 37, 43);
        menuStrip.AccessibleName = "主菜单";
    }

    internal static void ConfigureEscapeShortcutDisplay(ToolStripMenuItem menuItem)
    {
        ArgumentNullException.ThrowIfNull(menuItem);
        menuItem.ShortcutKeys = Keys.None;
        menuItem.ShortcutKeyDisplayString = "Esc";
    }

    internal static void ConfigureSettingsShortcutDisplay(
        ToolStripMenuItem menuItem)
    {
        ArgumentNullException.ThrowIfNull(menuItem);
        menuItem.ShortcutKeys = Keys.Control | Keys.Oemcomma;
        menuItem.ShortcutKeyDisplayString = "Ctrl + 逗号键";
    }

    private void ConfigureMainAssetMenu()
    {
        _mainAssetMenuItem.Text = "资产(&A)";
        var focusFilterItem = new ToolStripMenuItem("查找或筛选资产(&F)")
        {
            ShortcutKeyDisplayString = "Ctrl+F"
        };
        focusFilterItem.Click += (_, _) => FocusAssetFilter();
        var selectAllItem = new ToolStripMenuItem("全选当前页(&A)")
        {
            ShortcutKeyDisplayString = "Ctrl+A"
        };
        selectAllItem.Click += (_, _) =>
        {
            _mainTabControl.SelectedTab = _assetsTabPage;
            _assetGrid.Focus();
            SelectAllGridRows(_assetGrid);
        };
        var openLocationItem = new ToolStripMenuItem("打开文件位置(&O)")
        {
            ShortcutKeyDisplayString = "Enter"
        };
        openLocationItem.Click += (_, _) => OpenCurrentAssetFileLocation();
        var detailsItem = new ToolStripMenuItem("资产详情(&I)")
        {
            ShortcutKeyDisplayString = "Alt+Enter"
        };
        detailsItem.Click += (_, _) => ShowCurrentAssetDetails();
        var tagsItem = new ToolStripMenuItem("标签(&T)");
        var addToCollectionItem = new ToolStripMenuItem("加入项目(&L)");
        addToCollectionItem.Click += async (_, _) => await AddSelectedAssetsToCollectionAsync();
        var publishItem = new ToolStripMenuItem("发布到 OpenWeb(&P)");
        publishItem.Click += async (_, _) => await PublishSelectedArticleAsync();
        var copyItem = new ToolStripMenuItem("复制到 CDSI 工作目录(&C)");
        copyItem.Click += async (_, _) =>
            await TransferSelectedAssetsAsync(ManagedAssetTransferAction.Copy);
        var moveItem = new ToolStripMenuItem("移动到 CDSI 工作目录(&M)");
        moveItem.Click += async (_, _) =>
            await TransferSelectedAssetsAsync(ManagedAssetTransferAction.Move);
        var backupItem = new ToolStripMenuItem("同步到 OSS(&B)");
        backupItem.Click += async (_, _) => await SyncSelectedAssetsToProjectAsync();
        var restoreItem = new ToolStripMenuItem("从 OSS 取回(&R)");
        restoreItem.Click += async (_, _) => await RestoreSelectedAssetsFromOssAsync();
        var hideItem = new ToolStripMenuItem("从资产列表中移除（不删除）(&H)")
        {
            ShortcutKeyDisplayString = "Delete"
        };
        hideItem.Click += async (_, _) => await HideSelectedAssetsFromListAsync();
        _mainAssetMenuItem.DropDownItems.AddRange(
            [
                focusFilterItem,
                selectAllItem,
                new ToolStripSeparator(),
                openLocationItem,
                detailsItem,
                tagsItem,
                addToCollectionItem,
                publishItem,
                new ToolStripSeparator(),
                copyItem,
                moveItem,
                new ToolStripSeparator(),
                backupItem,
                restoreItem,
                new ToolStripSeparator(),
                hideItem
            ]);
        _mainAssetMenuItem.DropDownOpening += (_, _) =>
        {
            var selected = GetSelectedAssets();
            var canOperate = !_isBusy && selected.Count > 0 && selected.All(asset =>
                asset.LocationStatus == AssetLocationStatus.Available);
            _mainAssetMenuItem.Enabled = !_isBusy;
            focusFilterItem.Enabled = !_isBusy;
            selectAllItem.Enabled = !_isBusy && _assetGrid.Rows.Count > 0;
            openLocationItem.Enabled = !_isBusy && _assetGrid.CurrentRow?.Tag is AssetListItem;
            detailsItem.Enabled = !_isBusy && _assetGrid.CurrentRow?.Tag is AssetListItem;
            ConfigureAssetTagMenu(tagsItem, selected);
            addToCollectionItem.Enabled = !_isBusy && selected.Count > 0;
            publishItem.Enabled = selected.Count == 1 &&
                canOperate &&
                _openWebPublishingService.Supports(selected[0].Path);
            copyItem.Enabled = canOperate;
            moveItem.Enabled = canOperate;
            backupItem.Enabled = canOperate;
            restoreItem.Enabled = !_isBusy && selected.Count > 0 &&
                selected.All(asset => asset.HasHealthyObjectStorageBackup);
            hideItem.Enabled = !_isBusy && selected.Count > 0;
        };
    }

    private ToolStripMenuItem CreateTabMenuItem(
        string text,
        TabPage tabPage,
        Keys shortcutKeys)
    {
        var item = new ToolStripMenuItem(text)
        {
            ShortcutKeys = shortcutKeys,
            Tag = tabPage
        };
        item.Click += (_, _) => _mainTabControl.SelectedTab = tabPage;
        return item;
    }

    private static ToolStripMenuItem CreateMenuItem(
        string text,
        EventHandler clickHandler)
    {
        var item = new ToolStripMenuItem(text);
        item.Click += clickHandler;
        return item;
    }

    private async Task OpenWorkspaceDirectoryAsync()
    {
        try
        {
            var workspace = await _workspaceService.GetAsync();
            if (workspace is null)
            {
                MessageBox.Show(
                    this,
                    "尚未配置 CDSI 工作目录。",
                    "CDSI Beacon",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            OpenDirectoryPath(workspace.Path, "无法打开 CDSI 工作目录");
        }
        catch (Exception exception)
        {
            ShowError("无法读取 CDSI 工作目录", exception);
        }
    }

    private void OpenDirectoryPath(string path, string errorTitle)
    {
        try
        {
            Directory.CreateDirectory(path);
            using var process = Process.Start(CreateOpenDirectoryStartInfo(path));
        }
        catch (Exception exception)
        {
            ShowError(errorTitle, exception);
        }
    }

    private void OpenBundledDocument(string filename)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, filename);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("应用文档不存在。", path);
            }

            using var process = Process.Start(CreateOpenDocumentStartInfo(path));
        }
        catch (Exception exception)
        {
            ShowError("无法打开应用文档", exception);
        }
    }

    internal static ProcessStartInfo CreateOpenDocumentStartInfo(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return new ProcessStartInfo
        {
            FileName = Path.GetFullPath(path),
            UseShellExecute = true
        };
    }

    private void ShowDataSafetyInformation()
    {
        MessageBox.Show(
            this,
            "扫描、索引、哈希、标签和资产清单默认只在本机处理。\n\n" +
            "只有在您明确执行 OSS 备份、OSS 取回或 OpenWeb 发布时，应用才进行相应网络传输。\n\n" +
            "标签和清单不会修改、移动或重命名源文件；AccessKey Secret、WordPress 密码和 Git 密码保存在当前 Windows 用户的凭据管理器中，Beacon 不读取 SSH 私钥。",
            "数据安全与隐私",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void ShowLegalDocuments(LegalDocumentPage initialPage)
    {
        try
        {
            using var dialog = LegalDocumentsForm.LoadFromDirectory(
                AppContext.BaseDirectory,
                initialPage);
            dialog.ShowDialog(this);
        }
        catch (Exception exception)
        {
            ShowError("无法打开许可信息", exception);
        }
    }

    private void ShowAboutDialog()
    {
        using var dialog = new AboutForm(GetApplicationVersion(), _clientId);
        dialog.ShowDialog(this);
    }

    private void ShowTaskCenter()
    {
        using var dialog = new TaskCenterForm(
            CreateTaskCenterSnapshot,
            () => _scanCancellation?.Cancel());
        dialog.ShowDialog(this);
    }

    private void ShowRuntimeLog()
    {
        _runtimeLog.WriteInformation("打开运行日志窗口");
        using var dialog = new RuntimeLogForm(_runtimeLog);
        dialog.ShowDialog(this);
    }

    private TaskCenterSnapshot CreateTaskCenterSnapshot()
    {
        var progressPercent = _progressBar.Style == ProgressBarStyle.Marquee
            ? null
            : (int?)Math.Clamp(_progressBar.Value / 10, 0, 100);
        return new TaskCenterSnapshot(
            _statusLabel.Text ?? string.Empty,
            _progressLabel.Text ?? string.Empty,
            _currentPathLabel.Text ?? string.Empty,
            progressPercent,
            _progressBar.Style == ProgressBarStyle.Marquee,
            _canCancelCurrentTask,
            _databaseStatusLabel.Text ?? string.Empty);
    }

    private void UpdateMainMenuState()
    {
        _startStandardScanMenuItem.Enabled = !_isBusy;
        _startFullScanMenuItem.Enabled = !_isBusy;
        _cancelScanMenuItem.Enabled = _canCancelCurrentTask;
        _refreshAssetsMenuItem.Enabled = !_isBusy;
        _createProjectMenuItem.Enabled = !_isBusy;
        _fileSettingsMenuItem.Enabled = !_isBusy;
        _backupDatabaseMenuItem.Enabled = !_isBusy && !_databaseBackupInProgress;
        _openDatabaseBackupDirectoryMenuItem.Enabled = !_databaseBackupInProgress;
        _settingsMenuItem.Enabled = !_isBusy;
        _mainAssetMenuItem.Enabled = !_isBusy;
    }

    internal static void ResetGridColumnWidths(DataGridView grid)
    {
        ArgumentNullException.ThrowIfNull(grid);
        foreach (DataGridViewColumn column in grid.Columns)
        {
            if (column.Tag is int initialWidth)
            {
                column.Width = initialWidth;
            }
        }
    }
}
