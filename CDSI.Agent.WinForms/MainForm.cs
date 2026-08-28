using System.Reflection;
using CDSI.Agent.Application.Assets;
using CDSI.Agent.Application.Collections;
using CDSI.Agent.Application.Git;
using CDSI.Agent.Application.Metadata;
using CDSI.Agent.Application.OpenWeb;
using CDSI.Agent.Application.Fingerprints;
using CDSI.Agent.Application.Scanning;
using CDSI.Agent.Application.Storage;
using CDSI.Agent.Application.Transfers;
using CDSI.Agent.Application.Workspaces;
using CDSI.Agent.Core.Assets;
using CDSI.Agent.Core.Fingerprints;
using CDSI.Agent.Core.Metadata;
using CDSI.Agent.Core.Scanning;
using CDSI.Agent.Core.Transfers;
using CDSI.Agent.Infrastructure.Persistence;

namespace CDSI.Agent.WinForms;

public sealed partial class MainForm : Form
{
    private readonly ScanApplicationService _scanService;
    private readonly WorkspaceApplicationService _workspaceService;
    private readonly ScanRootManagementService _scanRootService;
    private readonly ObjectStorageProfileService _storageService;
    private readonly OpenWebSettingsService _openWebSettingsService;
    private readonly GitProfileService _gitProfileService;
    private readonly GitProjectSyncService _gitProjectSyncService;
    private readonly OpenWebArticlePublishingService _openWebPublishingService;
    private readonly ObjectStorageBackupService _objectStorageBackupService;
    private readonly ObjectStorageRestoreService _objectStorageRestoreService;
    private readonly ObjectStorageManagementService _objectStorageManagementService;
    private readonly ManagedAssetTransferService _transferService;
    private readonly RuntimeLogService _runtimeLog;
    private readonly FingerprintApplicationService _fingerprintService;
    private readonly MetadataExtractionApplicationService _metadataService;
    private readonly LocalDatabaseBackupService _localDatabaseBackupService;
    private readonly GiteeApplicationUpdateChecker _applicationUpdateChecker;
    private readonly string _clientId;
    private readonly System.Windows.Forms.Timer _databaseBackupTimer = new();
    private readonly TableLayoutPanel _progressPanel = new();
    private readonly RowStyle _progressPanelRowStyle = new(SizeType.Absolute, 0);
    private readonly ProgressBar _progressBar = new();
    private readonly Label _progressLabel = new();
    private readonly Label _currentPathLabel = new();
    private readonly Label _assetDetailTitleLabel = new();
    private readonly Label _assetDetailSummaryLabel = new();
    private readonly DataGridView _assetGrid = new();
    private readonly DataGridView _duplicateGrid = new();
    private readonly ContextMenuStrip _assetContextMenu = new();
    private readonly ToolStripMenuItem _copyToWorkspaceMenuItem = new();
    private readonly ToolStripMenuItem _moveToWorkspaceMenuItem = new();
    private readonly ToolStripMenuItem _backupToOssMenuItem = new();
    private readonly TabPage _assetsTabPage = new("全部资产");
    private readonly TabPage _duplicatesTabPage = new("重复文件");
    private readonly ToolStripStatusLabel _statusLabel = new();
    private readonly ToolStripStatusLabel _databaseStatusLabel = new();
    private readonly TabControl _mainTabControl = new();
    private readonly string _dataDirectory;
    private CancellationTokenSource? _scanCancellation;
    private bool _canCancelCurrentTask;

    public MainForm(
        ScanApplicationService scanService,
        FingerprintApplicationService fingerprintService,
        MetadataExtractionApplicationService metadataService,
        WorkspaceApplicationService workspaceService,
        ScanRootManagementService scanRootService,
        LocalVolumeReconciliationService volumeReconciliationService,
        ObjectStorageProfileService storageService,
        OpenWebSettingsService openWebSettingsService,
        GitProfileService gitProfileService,
        OpenWebArticlePublishingService openWebPublishingService,
        ObjectStorageBackupService objectStorageBackupService,
        ObjectStorageRestoreService objectStorageRestoreService,
        ObjectStorageManagementService objectStorageManagementService,
        AssetCollectionService assetCollectionService,
        GitProjectSyncService gitProjectSyncService,
        AssetTagService assetTagService,
        ManagedAssetTransferService transferService,
        LocalDatabaseBackupService localDatabaseBackupService,
        GiteeApplicationUpdateChecker applicationUpdateChecker,
        string clientId,
        string dataDirectory,
        RuntimeLogService runtimeLog)
    {
        _dataDirectory = Path.GetFullPath(dataDirectory);
        _scanService = scanService;
        _fingerprintService = fingerprintService;
        _metadataService = metadataService;
        _workspaceService = workspaceService;
        _scanRootService = scanRootService;
        _volumeReconciliationService = volumeReconciliationService;
        _storageService = storageService;
        _openWebSettingsService = openWebSettingsService;
        _gitProfileService = gitProfileService;
        _openWebPublishingService = openWebPublishingService;
        _objectStorageBackupService = objectStorageBackupService;
        _objectStorageRestoreService = objectStorageRestoreService;
        _objectStorageManagementService = objectStorageManagementService;
        _assetCollectionService = assetCollectionService;
        _gitProjectSyncService = gitProjectSyncService;
        _assetTagService = assetTagService;
        _transferService = transferService;
        _localDatabaseBackupService = localDatabaseBackupService;
        _applicationUpdateChecker = applicationUpdateChecker ??
            throw new ArgumentNullException(nameof(applicationUpdateChecker));
        _clientId = string.IsNullOrWhiteSpace(clientId)
            ? throw new ArgumentException("Client ID is required.", nameof(clientId))
            : clientId;
        _runtimeLog = runtimeLog ?? throw new ArgumentNullException(nameof(runtimeLog));
        InitializeLayout(_dataDirectory);

        _databaseBackupTimer.Interval = (int)TimeSpan.FromHours(1).TotalMilliseconds;
        _databaseBackupTimer.Tick += DatabaseBackupTimer_Tick;
        ConfigureIdleScanScheduler();

        Shown += MainForm_Shown;
        FormClosing += MainForm_FormClosing;
    }

    private void InitializeLayout(string dataDirectory)
    {
        SuspendLayout();

        var applicationVersion = GetApplicationVersion();
        Text = $"CDSI Beacon v{applicationVersion}";
        ConfigureStartupWindow(this);
        BackColor = Color.FromArgb(247, 248, 250);
        Font = new Font("Segoe UI", 9F);
        AutoScaleMode = AutoScaleMode.Dpi;
        KeyPreview = true;
        KeyDown += MainForm_KeyDown;
        ConfigureMainMenu();

        var mainLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(0),
            BackColor = BackColor
        };
        mainLayout.Controls.Add(_mainMenuStrip, 0, 0);

        var header = CreateMainBanner(applicationVersion);
        mainLayout.Controls.Add(header, 0, 1);

        _progressPanel.Dock = DockStyle.Fill;
        _progressPanel.ColumnCount = 2;
        _progressPanel.RowCount = 2;
        _progressPanel.Padding = new Padding(28, 7, 28, 7);
        _progressPanel.BackColor = Color.FromArgb(247, 248, 250);
        _progressPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 250));
        _progressPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _progressPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        _progressPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));

        _progressLabel.AutoSize = true;
        _progressLabel.Text = "就绪";
        _progressLabel.Font = new Font("Segoe UI Semibold", 9F);
        _progressLabel.ForeColor = Color.FromArgb(52, 61, 69);

        _currentPathLabel.Dock = DockStyle.Fill;
        _currentPathLabel.Text = "尚未扫描";
        _currentPathLabel.TextAlign = ContentAlignment.MiddleLeft;
        _currentPathLabel.AutoEllipsis = true;
        _currentPathLabel.ForeColor = Color.FromArgb(101, 111, 120);

        _progressBar.Dock = DockStyle.Fill;
        _progressBar.Height = 5;
        _progressBar.Style = ProgressBarStyle.Blocks;

        _progressPanel.Controls.Add(_progressLabel, 0, 0);
        _progressPanel.SetColumnSpan(_progressLabel, 2);
        _progressPanel.Controls.Add(_progressBar, 0, 1);
        _progressPanel.Controls.Add(_currentPathLabel, 1, 1);

        ConfigureAssetGrid();
        ConfigureDuplicateGrid();
        ConfigureAssetDirectoryTab();
        ConfigureAssetCollectionTab();
        ConfigureCloudBackupManagementTab();
        ConfigureGitProjectManagementTab();
        ConfigureStatisticsTab();
        _assetGrid.SelectionChanged += AssetGrid_SelectionChanged;

        _assetsTabPage.Padding = new Padding(0);
        _assetsTabPage.BackColor = Color.White;
        var detailsPanel =
            CreateAssetDetailsPanel(
                _assetDetailTitleLabel,
                _assetDetailSummaryLabel);
        var assetTabLayout = CreateAssetTabLayout(
            ConfigureAssetFilterPanel(),
            _assetGrid,
            ConfigureAssetPagination(),
            detailsPanel);
        _assetsTabPage.Controls.Add(assetTabLayout);
        _duplicatesTabPage.Padding = new Padding(0);
        _duplicatesTabPage.BackColor = Color.White;
        _duplicatesTabPage.Controls.Add(_duplicateGrid);

        ConfigureMainTabs(
            _mainTabControl,
            [
                _assetsTabPage,
                _assetDirectoriesTabPage,
                _duplicatesTabPage,
                _collectionsTabPage,
                _cloudBackupsTabPage,
                _gitProjectsTabPage,
                _statisticsTabPage
            ]);

        var gridHost = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(28, 8, 28, 18),
            BackColor = BackColor
        };
        gridHost.Controls.Add(_mainTabControl);
        ConfigureMainContentLayout(
            mainLayout,
            gridHost,
            _progressPanel,
            _progressPanelRowStyle);

        var statusStrip = new StatusStrip
        {
            SizingGrip = false,
            BackColor = Color.White,
            Padding = new Padding(20, 0, 20, 0)
        };
        _statusLabel.Text = "正在初始化";
        _statusLabel.ForeColor = Color.FromArgb(72, 81, 89);
        _databaseStatusLabel.Text = $"数据目录: {dataDirectory}";
        _databaseStatusLabel.Spring = true;
        _databaseStatusLabel.TextAlign = ContentAlignment.MiddleRight;
        _databaseStatusLabel.ForeColor = Color.FromArgb(112, 121, 129);
        statusStrip.Items.Add(_statusLabel);
        statusStrip.Items.Add(_databaseStatusLabel);

        Controls.Add(mainLayout);
        Controls.Add(statusStrip);
        MainMenuStrip = _mainMenuStrip;
        ResumeLayout();
    }

    internal static void ConfigureStartupWindow(Form form)
    {
        ArgumentNullException.ThrowIfNull(form);
        form.StartPosition = FormStartPosition.CenterScreen;
        form.MinimumSize = new Size(920, 600);
        form.Size = new Size(1180, 760);
        form.WindowState = FormWindowState.Maximized;
    }

    internal static TableLayoutPanel CreateMainBanner(string applicationVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationVersion);

        var banner = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            GrowStyle = TableLayoutPanelGrowStyle.FixedSize,
            Margin = Padding.Empty,
            Padding = new Padding(28, 10, 28, 10),
            BackColor = Color.FromArgb(31, 37, 43),
            AccessibleName = "顶部 Banner"
        };
        banner.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        banner.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        banner.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        banner.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var titleLabel = new Label
        {
            AutoSize = true,
            Margin = Padding.Empty,
            Text = "CDSI Beacon",
            Font = new Font("Segoe UI Semibold", 18F),
            ForeColor = Color.White,
            AccessibleName = "应用名称"
        };
        var subtitleLabel = new Label
        {
            AutoSize = true,
            Margin = new Padding(0, 2, 0, 0),
            Text = "本地资产索引",
            Font = new Font("Segoe UI", 9F),
            ForeColor = Color.FromArgb(179, 190, 199),
            AccessibleName = "应用说明"
        };
        var versionLabel = new Label
        {
            AutoSize = true,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Margin = new Padding(20, 6, 0, 0),
            Text = $"v{applicationVersion}",
            TextAlign = ContentAlignment.TopRight,
            Font = new Font("Segoe UI Semibold", 9F),
            ForeColor = Color.FromArgb(179, 190, 199),
            AccessibleName = "应用版本"
        };

        banner.Controls.Add(titleLabel, 0, 0);
        banner.Controls.Add(subtitleLabel, 0, 1);
        banner.Controls.Add(versionLabel, 1, 0);
        banner.SetRowSpan(versionLabel, 2);
        return banner;
    }

    internal static void ConfigureMainContentLayout(
        TableLayoutPanel mainLayout,
        Control content,
        Control progress,
        RowStyle progressRowStyle)
    {
        ArgumentNullException.ThrowIfNull(mainLayout);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(progress);
        ArgumentNullException.ThrowIfNull(progressRowStyle);
        mainLayout.RowStyles.Clear();
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        progressRowStyle.SizeType = SizeType.Absolute;
        mainLayout.RowStyles.Add(progressRowStyle);
        mainLayout.Controls.Add(content, 0, 2);
        mainLayout.Controls.Add(progress, 0, 3);
        SetProgressVisibility(progress, progressRowStyle, visible: false);
    }

    internal static void SetProgressVisibility(
        Control progress,
        RowStyle progressRowStyle,
        bool visible)
    {
        ArgumentNullException.ThrowIfNull(progress);
        ArgumentNullException.ThrowIfNull(progressRowStyle);
        progressRowStyle.Height = visible ? 58 : 0;
        progress.Visible = visible;
    }

    internal static void ConfigureMainTabs(
        TabControl tabControl,
        IReadOnlyList<TabPage> tabPages)
    {
        ArgumentNullException.ThrowIfNull(tabControl);
        ArgumentNullException.ThrowIfNull(tabPages);
        tabControl.TabPages.Clear();
        tabControl.TabPages.AddRange(tabPages.ToArray());
        tabControl.Dock = DockStyle.Fill;
        tabControl.Padding = new Point(12, 5);
    }

    internal static TableLayoutPanel CreateAssetTabLayout(
        Control filterPanel,
        DataGridView assetGrid,
        Control paginationPanel,
        Control detailsPanel)
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        layout.Controls.Add(filterPanel, 0, 0);
        layout.Controls.Add(assetGrid, 0, 1);
        layout.Controls.Add(paginationPanel, 0, 2);
        layout.Controls.Add(detailsPanel, 0, 3);
        return layout;
    }

    internal static TableLayoutPanel CreateAssetDetailsPanel(
        Label titleLabel,
        Label summaryLabel)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            GrowStyle = TableLayoutPanelGrowStyle.FixedSize,
            Margin = Padding.Empty,
            Padding = new Padding(8, 4, 8, 4),
            BackColor = Color.White
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        titleLabel.Dock = DockStyle.Fill;
        titleLabel.Margin = Padding.Empty;
        titleLabel.Text = "未选择资产";
        titleLabel.AutoEllipsis = true;
        titleLabel.TextAlign = ContentAlignment.MiddleLeft;
        titleLabel.Font = new Font("Segoe UI Semibold", 9.5F);
        titleLabel.ForeColor = Color.FromArgb(31, 37, 43);
        titleLabel.AccessibleName = "资产标题";

        summaryLabel.Dock = DockStyle.Fill;
        summaryLabel.Margin = Padding.Empty;
        summaryLabel.Text = string.Empty;
        summaryLabel.AutoEllipsis = true;
        summaryLabel.TextAlign = ContentAlignment.MiddleLeft;
        summaryLabel.ForeColor = Color.FromArgb(88, 98, 106);
        summaryLabel.AccessibleName = "资产摘要";

        panel.Controls.Add(titleLabel, 0, 0);
        panel.Controls.Add(summaryLabel, 0, 1);
        return panel;
    }
    private void ConfigureAssetGrid()
    {
        ConfigureGrid(_assetGrid);
        EnableAssetMultiSelection(_assetGrid);
        ConfigureAssetGridColumns(_assetGrid);
        EnableFreeColumnResizing(_assetGrid);
        _assetGrid.Sorted += (_, _) => UpdateAssetRowNumbers(
            _assetGrid,
            CalculateAssetPagination(
                _assetTotalItems,
                _assetPageSize,
                _assetPageIndex).FirstItem);
        ConfigureAssetContextMenu();
    }

    internal static void ConfigureAssetGridColumns(DataGridView grid)
    {
        ArgumentNullException.ThrowIfNull(grid);
        grid.Columns.Add(CreateRowNumberColumn());
        grid.Columns.Add(CreateAssetIdColumn());
        grid.Columns.Add(CreateColumn("文件", 220, DataGridViewAutoSizeColumnMode.Fill, 24));
        var projectsColumn = CreateColumn("所属项目", 180);
        projectsColumn.Name = "Projects";
        grid.Columns.Add(projectsColumn);
        grid.Columns.Add(CreateBackupStatusColumn());
        grid.Columns.Add(CreateBackupTimeColumn());
        grid.Columns.Add(CreateColumn("标签", 180));
        grid.Columns.Add(CreateColumn("类型", 125));
        grid.Columns.Add(CreateFileSizeColumn());
        grid.Columns.Add(CreateSha256Column());
        grid.Columns.Add(CreateColumn("修改时间", 145));
        var indexedAtColumn = CreateColumn("索引时间", 145);
        indexedAtColumn.Name = "IndexedAt";
        grid.Columns.Add(indexedAtColumn);
        grid.Columns.Add(CreateColumn(
            "位置",
            320,
            DataGridViewAutoSizeColumnMode.Fill,
            42));
        grid.Columns.Add(CreateColumn(
            "媒体信息",
            220,
            DataGridViewAutoSizeColumnMode.Fill,
            34,
            minimumWidth: 220));
        grid.Columns.Add(CreateColumn("状态", 80));
        grid.AllowUserToOrderColumns = true;
    }

    internal static void EnableAssetMultiSelection(DataGridView grid)
    {
        ArgumentNullException.ThrowIfNull(grid);
        grid.MultiSelect = true;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
    }

    internal static void EnableFreeColumnResizing(DataGridView grid)
    {
        ArgumentNullException.ThrowIfNull(grid);
        grid.AllowUserToResizeColumns = true;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        grid.ScrollBars = ScrollBars.Both;

        foreach (DataGridViewColumn column in grid.Columns)
        {
            var initialWidth = column.Width;
            column.Tag ??= initialWidth;
            column.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            column.MinimumWidth = 40;
            column.Width = Math.Max(initialWidth, column.MinimumWidth);
            column.Resizable = DataGridViewTriState.True;
        }
    }

    private void ConfigureDuplicateGrid()
    {
        ConfigureGrid(_duplicateGrid);
        _duplicateGrid.Columns.Add(CreateColumn("组", 60));
        _duplicateGrid.Columns.Add(CreateColumn("SHA-256", 125));
        _duplicateGrid.Columns.Add(CreateColumn("文件", 220, DataGridViewAutoSizeColumnMode.Fill, 24));
        _duplicateGrid.Columns.Add(CreateFileSizeColumn());
        _duplicateGrid.Columns.Add(CreateColumn("位置", 360, DataGridViewAutoSizeColumnMode.Fill, 48));
        _duplicateGrid.Columns.Add(CreateColumn("状态", 80));
        EnableFreeColumnResizing(_duplicateGrid);
        ConfigureDuplicateContextMenu();
    }

    private static void ConfigureGrid(DataGridView grid)
    {
        grid.Dock = DockStyle.Fill;
        grid.BackgroundColor = Color.White;
        grid.BorderStyle = BorderStyle.FixedSingle;
        grid.ReadOnly = true;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.AllowUserToResizeRows = false;
        grid.AutoGenerateColumns = false;
        grid.MultiSelect = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.ShowCellToolTips = true;
        grid.RowHeadersVisible = false;
        grid.RowTemplate.Height = 30;
        grid.ColumnHeadersHeight = 36;
        grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(239, 242, 244);
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(52, 61, 69);
        grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI Semibold", 9F);
        grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(220, 235, 227);
        grid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(31, 37, 43);
        grid.DefaultCellStyle.Padding = new Padding(4, 0, 4, 0);
        grid.GridColor = Color.FromArgb(229, 232, 235);
        grid.CellFormatting += Grid_CellFormatting;
    }

    private static DataGridViewColumn CreateColumn(
        string title,
        int width,
        DataGridViewAutoSizeColumnMode sizeMode = DataGridViewAutoSizeColumnMode.None,
        float fillWeight = 100,
        int? minimumWidth = null)
    {
        return new DataGridViewTextBoxColumn
        {
            HeaderText = title,
            Width = width,
            MinimumWidth = minimumWidth ?? Math.Min(width, 80),
            AutoSizeMode = sizeMode,
            FillWeight = fillWeight,
            SortMode = DataGridViewColumnSortMode.Automatic
        };
    }

    internal static DataGridViewColumn CreateFileSizeColumn()
    {
        var column = CreateColumn("大小", 90);
        column.Name = "FileSizeBytes";
        column.ValueType = typeof(long);
        column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        return column;
    }

    internal static DataGridViewColumn CreateSha256Column()
    {
        var column = CreateColumn(
            "文件校验值（SHA256）",
            500,
            minimumWidth: 240);
        column.Name = "Sha256";
        column.ValueType = typeof(string);
        column.DefaultCellStyle.Font = new Font("Consolas", 9F);
        return column;
    }

    internal static string FormatSha256ForList(string? sha256)
    {
        return string.IsNullOrWhiteSpace(sha256) ? "-" : sha256;
    }

    internal static DataGridViewColumn CreateRowNumberColumn()
    {
        var column = CreateColumn("行号", 62, minimumWidth: 54);
        column.Name = "RowNumber";
        column.ValueType = typeof(long);
        column.SortMode = DataGridViewColumnSortMode.NotSortable;
        column.Frozen = true;
        column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        return column;
    }

    internal static void UpdateAssetRowNumbers(
        DataGridView grid,
        long firstItemNumber)
    {
        ArgumentNullException.ThrowIfNull(grid);
        if (!grid.Columns.Contains("RowNumber"))
        {
            return;
        }

        var firstNumber = Math.Max(1, firstItemNumber);
        foreach (DataGridViewRow row in grid.Rows)
        {
            row.Cells["RowNumber"].Value = firstNumber + row.Index;
        }
    }

    internal static DataGridViewColumn CreateAssetIdColumn()
    {
        var column = CreateColumn("资产 ID", 118, minimumWidth: 96);
        column.Name = "AssetId";
        column.ValueType = typeof(string);
        return column;
    }

    internal static string FormatAssetIdForList(Guid assetId)
    {
        return assetId.ToString("N")[^12..];
    }

    internal static string FormatAssetProjects(IReadOnlyList<string> projectNames)
    {
        ArgumentNullException.ThrowIfNull(projectNames);
        return projectNames.Count == 0
            ? "无"
            : string.Join("、", projectNames);
    }

    internal static DataGridViewColumn CreateBackupStatusColumn()
    {
        var column = CreateColumn("备份状态", 120);
        column.Name = "BackupStatus";
        return column;
    }

    internal static DataGridViewColumn CreateBackupTimeColumn()
    {
        var column = CreateColumn("备份时间", 145);
        column.Name = "BackupTime";
        return column;
    }

    internal static string FormatBackupStatus(
        bool hasHealthyBackup,
        IReadOnlyList<string> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        if (!hasHealthyBackup)
        {
            return "未备份";
        }

        var labels = providers
            .Where(provider => !string.IsNullOrWhiteSpace(provider))
            .Select(FormatBackupProvider)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return labels.Length == 0
            ? "已备份"
            : string.Join("、", labels);
    }

    internal static string FormatBackupTime(DateTimeOffset? backupTime)
    {
        return backupTime?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "-";
    }

    private static string FormatBackupProvider(string provider)
    {
        return provider.Trim().ToUpperInvariant() switch
        {
            "ALIYUNOSS" or "ALIYUN OSS" or "OSS" => "OSS",
            "QINIU" or "QINIUKODO" or "QINIU KODO" => "七牛",
            "TENCENT" or "TENCENTCOS" or "TENCENT COS" or "COS" => "COS",
            "S3" or "AMAZONS3" or "S3COMPATIBLE" => "S3",
            _ => provider.Trim()
        };
    }

    internal static void Grid_CellFormatting(
        object? sender,
        DataGridViewCellFormattingEventArgs e)
    {
        if (sender is not DataGridView grid ||
            e.ColumnIndex < 0 ||
            e.ColumnIndex >= grid.Columns.Count)
        {
            return;
        }

        var columnName = grid.Columns[e.ColumnIndex].Name;
        if (string.Equals(
                columnName,
                "FileSizeBytes",
                StringComparison.Ordinal) &&
            e.Value is long bytes)
        {
            e.Value = FormatFileSize(bytes);
            e.FormattingApplied = true;
            return;
        }

        if (string.Equals(
                columnName,
                "BackupStatus",
                StringComparison.Ordinal) &&
            e.Value is string backupStatus &&
            !string.Equals(backupStatus, "未备份", StringComparison.Ordinal))
        {
            var healthyColor = Color.FromArgb(24, 121, 78);
            e.CellStyle.ForeColor = healthyColor;
            e.CellStyle.SelectionForeColor = healthyColor;
        }
    }

    private async void MainForm_Shown(object? sender, EventArgs e)
    {
        SetBusy(true, allowCancel: false);
        try
        {
            await _scanService.InitializeAsync();
            var workspace = await _workspaceService.GetAsync();
            if (workspace is null)
            {
                using var setupForm = new FirstRunSetupForm();
                if (setupForm.ShowDialog(this) != DialogResult.OK)
                {
                    Close();
                    return;
                }

                var setupResult = await _workspaceService.ConfigureAsync(
                    setupForm.SelectedPath);
                workspace = setupResult.Workspace;
            }

            await TryCreateAutomaticDatabaseBackupAsync(workspace.Path);
            _databaseBackupTimer.Start();
            _idleScanTimer.Start();

            var volumeResult = await _volumeReconciliationService.ReconcileAsync();
            EnableLocalVolumeMonitoring();
            await RefreshAssetsAsync();
            _statusLabel.Text = volumeResult.HasChanges
                ? FormatVolumeReconciliationStatus(volumeResult)
                : "就绪";
        }
        catch (Exception exception)
        {
            _statusLabel.Text = "初始化失败";
            ShowError("无法初始化应用", exception);
        }
        finally
        {
            SetBusy(false);
        }

        await CheckForUpdatesAsync(showCurrentStatus: false);
    }

    private async Task OpenSettingsAsync()
    {
        using var settingsForm = new SettingsForm(
            _workspaceService,
            _scanRootService,
            _storageService,
            _openWebSettingsService,
            _gitProfileService);
        var settingsResult = settingsForm.ShowDialog(this);
        await _volumeReconciliationService.ReconcileAsync();
        await RefreshAssetCollectionsAsync();
        await TryCreateAutomaticDatabaseBackupAsync();
        if (settingsResult == DialogResult.OK &&
            settingsForm.InitialScanRootIds.Count > 0)
        {
            await RunScanPipelineAsync(
                settingsForm.InitialScanRootIds,
                isInitialScan: true,
                fingerprintMode: FingerprintMode.DuplicateCandidates);
        }
    }

    private async Task StartConfiguredScanAsync(FingerprintMode fingerprintMode)
    {
        var configuredRoots = await _scanService.ListScanRootsAsync();
        if (!configuredRoots.Any(root => root.Enabled))
        {
            MessageBox.Show(
                this,
                "没有已启用的扫描目录。",
                "CDSI Beacon",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        await RunScanPipelineAsync(
            scanRootIds: null,
            isInitialScan: false,
            fingerprintMode: fingerprintMode);
    }

    private async Task RunScanPipelineAsync(
        IReadOnlyCollection<Guid>? scanRootIds,
        bool isInitialScan,
        FingerprintMode fingerprintMode,
        bool isIdleScan = false)
    {
        var selectedRootIds = scanRootIds?.Distinct().ToArray();
        if (selectedRootIds is { Length: 0 })
        {
            return;
        }

        _scanCancellation?.Dispose();
        _scanCancellation = new CancellationTokenSource();
        var scanProgress = new Progress<ScanProgress>(UpdateScanProgress);
        var fingerprintProgress = new Progress<FingerprintProgress>(UpdateFingerprintProgress);
        var metadataProgress = new Progress<MetadataProgress>(UpdateMetadataProgress);

        SetBusy(true);
        _progressBar.Style = ProgressBarStyle.Marquee;
        _progressBar.MarqueeAnimationSpeed = 24;
        _statusLabel.Text = isIdleScan
            ? "正在执行空闲扫描"
            : isInitialScan
                ? "正在扫描新增目录"
                : "正在扫描";

        try
        {
            var scanSummary = await Task.Run(
                () => selectedRootIds is null
                    ? _scanService.ScanConfiguredRootsAsync(
                        scanProgress,
                        _scanCancellation.Token)
                    : _scanService.ScanRootsAsync(
                        selectedRootIds,
                        scanProgress,
                        _scanCancellation.Token),
                _scanCancellation.Token);

            await RefreshAssetsAsync();
            if (scanSummary.Cancelled)
            {
                _statusLabel.Text = isIdleScan
                    ? "空闲扫描已取消"
                    : isInitialScan
                        ? "新增目录扫描已取消"
                        : "扫描已取消";
                return;
            }

            _progressBar.MarqueeAnimationSpeed = 0;
            _progressBar.Style = ProgressBarStyle.Continuous;
            _progressBar.Minimum = 0;
            _progressBar.Maximum = 1_000;
            _progressBar.Value = 0;
            _statusLabel.Text = "正在提取元数据";

            var metadataSummary = await Task.Run(
                () => _metadataService.ProcessPendingAsync(
                    metadataProgress,
                    _scanCancellation.Token),
                _scanCancellation.Token);

            await RefreshAssetsAsync();
            if (metadataSummary.Cancelled)
            {
                _statusLabel.Text =
                    $"元数据提取已取消，已完成 {metadataSummary.ExtractedFiles:N0} 个文件";
                return;
            }

            var mode = fingerprintMode;
            _progressBar.MarqueeAnimationSpeed = 0;
            _progressBar.Style = ProgressBarStyle.Continuous;
            _progressBar.Minimum = 0;
            _progressBar.Maximum = 1_000;
            _progressBar.Value = 0;
            _statusLabel.Text = mode == FingerprintMode.Complete
                ? "正在进行完整校验"
                : "正在检查重复候选";

            var fingerprintSummary = await Task.Run(
                () => _fingerprintService.ProcessPendingAsync(
                    mode,
                    fingerprintProgress,
                    _scanCancellation.Token),
                _scanCancellation.Token);

            await RefreshAssetsAsync();
            var completedText = isIdleScan
                ? "空闲扫描完成"
                : isInitialScan
                    ? "新增目录扫描完成"
                    : "扫描完成";
            _statusLabel.Text = fingerprintSummary.Cancelled
                ? $"哈希已取消，已完成 {fingerprintSummary.FingerprintedFiles:N0} 个文件"
                : $"{completedText}，目录 {scanSummary.RootsScanned:N0}/{scanSummary.RootsConfigured:N0}，已索引 {scanSummary.FilesIndexed:N0} 个文件，元数据 {metadataSummary.ExtractedFiles:N0}，哈希 {fingerprintSummary.FingerprintedFiles:N0}";
        }
        catch (OperationCanceledException)
        {
            _statusLabel.Text = "操作已取消";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _statusLabel.Text = "扫描失败";
            if (isIdleScan)
            {
                _runtimeLog.WriteError("空闲扫描未能完成", exception);
            }
            else
            {
                ShowError("扫描未能完成", exception);
            }
        }
        finally
        {
            _progressBar.MarqueeAnimationSpeed = 0;
            _progressBar.Style = ProgressBarStyle.Blocks;
            SetBusy(false);
        }
    }

    private void UpdateScanProgress(ScanProgress progress)
    {
        _progressLabel.Text =
            $"发现 {progress.FilesDiscovered:N0}  ·  已索引 {progress.FilesIndexed:N0}  ·  错误 {progress.Errors:N0}";
        _currentPathLabel.Text = progress.CurrentPath ?? progress.Message ?? string.Empty;

        if (progress.Stage == ScanStage.Failed)
        {
            _currentPathLabel.Text = progress.Message ?? "扫描失败";
        }
    }

    private void UpdateMetadataProgress(MetadataProgress progress)
    {
        _progressLabel.Text =
            $"元数据 {progress.CompletedFiles:N0}/{progress.TotalFiles:N0}  ·  已提取 {progress.ExtractedFiles:N0}  ·  不支持 {progress.UnsupportedFiles:N0}  ·  错误 {progress.Errors:N0}";
        _currentPathLabel.Text = progress.Message ?? progress.CurrentPath ?? string.Empty;

        _progressBar.Value = progress.TotalFiles == 0
            ? 0
            : (int)Math.Clamp(
                progress.CompletedFiles * 1_000d / progress.TotalFiles,
                0d,
                1_000d);
    }

    private void UpdateFingerprintProgress(FingerprintProgress progress)
    {
        var modeText = progress.Mode == FingerprintMode.Complete
            ? "完整校验"
            : "重复候选";
        _progressLabel.Text =
            $"{modeText} {progress.CompletedFiles:N0}/{progress.TotalFiles:N0}  ·  已哈希 {progress.FingerprintedFiles:N0}  ·  {FormatFileSize(progress.ProcessedBytes)}/{FormatFileSize(progress.TotalBytes)}  ·  {FormatFileSize((long)progress.BytesPerSecond)}/s  ·  错误 {progress.Errors:N0}";
        _currentPathLabel.Text = progress.Message ?? progress.CurrentPath ?? string.Empty;

        _progressBar.Value = progress.TotalBytes == 0
            ? 0
            : (int)Math.Clamp(
                progress.ProcessedBytes * 1_000d / progress.TotalBytes,
                0d,
                1_000d);
    }

    private async Task RefreshAssetsAsync()
    {
        var filter = _assetListFilter;
        var assetCountTask = _scanService.GetAssetListCountAsync(filter);
        var totalAssetCountTask = filter.IsEmpty
            ? assetCountTask
            : _scanService.GetAssetListCountAsync();
        var duplicateGroupsTask = _scanService.ListExactDuplicateGroupsAsync();
        var statisticsTask = _scanService.GetLocalAssetStatisticsAsync();
        var assetDirectoriesTask = _scanService.ListAssetDirectoriesAsync();
        var selectedFileType = _assetFileTypeFilterComboBox.SelectedItem is
            AssetFileTypeFilterChoice selectedType
                ? selectedType.Value
                : filter.FileType;
        var assetExtensionsTask = _scanService.ListAssetExtensionsAsync(
            selectedFileType);
        var assetTagsTask = _assetTagService.ListAsync();
        await Task.WhenAll(
            assetCountTask,
            totalAssetCountTask,
            duplicateGroupsTask,
            statisticsTask,
            assetDirectoriesTask,
            assetExtensionsTask,
            assetTagsTask);

        var assetCount = await assetCountTask;
        var totalAssetCount = await totalAssetCountTask;
        var pagination = CalculateAssetPagination(
            assetCount,
            _assetPageSize,
            _assetPageIndex);
        _assetPageIndex = pagination.PageIndex;
        var assets = await _scanService.ListAssetsAsync(
            filter,
            _assetPageSize,
            pagination.Offset);
        var duplicateGroups = await duplicateGroupsTask;
        var statistics = await statisticsTask;
        var assetDirectories = await assetDirectoriesTask;
        var assetExtensions = await assetExtensionsTask;
        _knownAssetTags = await assetTagsTask;
        _assetGrid.Rows.Clear();
        _duplicateGrid.Rows.Clear();

        foreach (var asset in assets)
        {
            var rowIndex = _assetGrid.Rows.Add(
                pagination.FirstItem + _assetGrid.Rows.Count,
                FormatAssetIdForList(asset.AssetId),
                asset.OriginalFilename,
                FormatAssetProjects(asset.ProjectNames),
                FormatBackupStatus(
                    asset.HasHealthyObjectStorageBackup,
                    asset.HealthyBackupProviders),
                FormatBackupTime(asset.LatestHealthyBackupAt),
                string.Join("、", asset.Tags),
                asset.MimeType ?? "未知",
                asset.Size,
                FormatSha256ForList(asset.Sha256),
                asset.ModifiedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                asset.DiscoveredAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                asset.Path,
                FormatMetadata(asset.Metadata),
                FormatStatus(asset));
            _assetGrid.Rows[rowIndex].Tag = asset;
            _assetGrid.Rows[rowIndex].Cells["AssetId"].ToolTipText =
                asset.AssetId.ToString("D");
        }

        if (_assetGrid.Rows.Count > 0)
        {
            _assetGrid.CurrentCell = _assetGrid.Rows[0].Cells[0];
            _assetGrid.Rows[0].Selected = true;
            UpdateAssetDetails(_assetGrid.Rows[0].Tag as AssetListItem);
        }
        else
        {
            UpdateAssetDetails(null);
        }

        var groupNumber = 0;
        foreach (var group in duplicateGroups)
        {
            groupNumber++;
            foreach (var asset in group.Assets)
            {
                var rowIndex = _duplicateGrid.Rows.Add(
                    groupNumber,
                    group.Sha256[..12],
                    asset.OriginalFilename,
                    group.Size,
                    asset.Path,
                    FormatLocationStatus(asset.LocationStatus));
                _duplicateGrid.Rows[rowIndex].Tag = asset;
            }
        }

        UpdateStatisticsDashboard(statistics);

        UpdateAssetPaginationControls(assetCount);
        UpdateAssetFilterResult(assetCount, totalAssetCount);
        if (_assetFileTypeFilterComboBox.SelectedItem is
                AssetFileTypeFilterChoice currentFileType &&
            currentFileType.Value == selectedFileType)
        {
            RefreshAssetExtensionChoices(
                _assetExtensionFilterComboBox,
                assetExtensions,
                includeUnavailableSelection:
                    selectedFileType == filter.FileType &&
                    filter.Extension is not null);
        }
        RefreshAssetTagChoices(
            _assetTagFilterComboBox,
            _knownAssetTags,
            filter.TagId);
        RefreshAssetDirectories(assetDirectories);
        _assetsTabPage.Text = filter.IsEmpty
            ? $"全部资产 ({assetCount:N0})"
            : $"全部资产 ({assetCount:N0}/{totalAssetCount:N0})";
        await RefreshAssetCollectionsAsync();
        await RefreshCloudBackupsAsync();
        _duplicatesTabPage.Text = $"重复文件 ({duplicateGroups.Count:N0})";
        var assetCountStatus = filter.IsEmpty
            ? $"资产位置 {assetCount:N0}"
            : $"筛选资产 {assetCount:N0}/{totalAssetCount:N0}";
        _statusLabel.Text =
            $"{assetCountStatus}  ·  当前页 {assets.Count:N0}  ·  可用文件 {statistics.AvailableLocalFileCount:N0}  ·  重复组 {duplicateGroups.Count:N0}";
    }

    private void AssetGrid_SelectionChanged(object? sender, EventArgs e)
    {
        UpdateAssetDetails(_assetGrid.CurrentRow?.Tag as AssetListItem);
        UpdateMainMenuState();
    }

    private void UpdateAssetDetails(AssetListItem? asset)
    {
        if (asset is null)
        {
            _assetDetailTitleLabel.Text = "未选择资产";
            _assetDetailSummaryLabel.Text = string.Empty;
            return;
        }

        _assetDetailTitleLabel.Text = asset.OriginalFilename;
        _assetDetailSummaryLabel.Text = FormatAssetDetailSummary(asset);
    }

    internal static string FormatAssetDetailSummary(AssetListItem asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        var tagSummary = asset.Tags.Count == 0
            ? "无标签"
            : $"标签：{string.Join("、", asset.Tags)}";
        var backupStatus = FormatBackupStatus(
            asset.HasHealthyObjectStorageBackup,
            asset.HealthyBackupProviders);
        var backupSummary = asset.HasHealthyObjectStorageBackup
            ? $"备份：{backupStatus} · 时间 {FormatBackupTime(asset.LatestHealthyBackupAt)}"
            : "备份：未备份";
        return string.Join(
            "  |  ",
            $"{asset.MimeType ?? "未知类型"} · {FormatFileSize(asset.Size)} · 修改 {asset.ModifiedAt.ToLocalTime():yyyy-MM-dd HH:mm} · 索引 {asset.DiscoveredAt.ToLocalTime():yyyy-MM-dd HH:mm}",
            tagSummary,
            backupSummary,
            asset.Path);
    }

    private void SetBusy(bool busy, bool allowCancel = true)
    {
        _isBusy = busy;
        _canCancelCurrentTask = ShouldAllowTaskCancellation(busy, allowCancel);
        SetProgressVisibility(_progressPanel, _progressPanelRowStyle, busy);
        _assetGrid.Enabled = !busy;
        _assetContextMenu.Enabled = !busy;
        UpdateAssetPaginationControls(_assetTotalItems);
        _collectionGrid.Enabled = !busy;
        _collectionMemberGrid.Enabled = !busy;
        _createCollectionButton.Enabled = !busy;
        _assetDirectoryGrid.Enabled = !busy;
        _cloudBackupProjectGrid.Enabled = !busy;
        _cloudBackupGrid.Enabled = !busy;
        _cloudBackupSearchTextBox.Enabled = !busy;
        _searchCloudBackupsButton.Enabled = !busy;
        _refreshCloudBackupsButton.Enabled = !busy;
        UpdateCollectionActionState();
        UpdateAssetDirectoryActionState();
        UpdateCloudBackupActionState();
        UpdateAssetFilterControlState();
        UpdateMainMenuState();
        UseWaitCursor = busy && !allowCancel;
    }

    internal static bool ShouldAllowTaskCancellation(bool busy, bool allowCancel)
    {
        return busy && allowCancel;
    }

    internal static string FormatStatus(AssetListItem asset)
    {
        if (asset.LocationStatus == AssetLocationStatus.Offline)
        {
            return "设备离线";
        }

        if (asset.LocationStatus == AssetLocationStatus.Unverified)
        {
            return "位置待确认";
        }

        if (asset.LocationStatus == AssetLocationStatus.Missing)
        {
            return "位置缺失";
        }

        if (asset.LocationOwnership == AssetLocationOwnership.Managed)
        {
            return "工作目录";
        }

        return asset.Status switch
        {
            AssetStatus.Indexed => "已索引",
            AssetStatus.Discovered => "已发现",
            AssetStatus.Error => "错误",
            _ => asset.Status.ToString()
        };
    }

    internal static string FormatLocationStatus(AssetLocationStatus status)
    {
        return status switch
        {
            AssetLocationStatus.Available => "可用",
            AssetLocationStatus.Missing => "位置缺失",
            AssetLocationStatus.Offline => "设备离线",
            AssetLocationStatus.Unverified => "位置待确认",
            _ => status.ToString()
        };
    }

    internal static string FormatMetadata(AssetMetadata? metadata)
    {
        if (metadata is null)
        {
            return "待提取";
        }

        if (metadata.Status == MetadataExtractionStatus.Unsupported)
        {
            return "无专用元数据";
        }

        if (metadata.Status == MetadataExtractionStatus.Error)
        {
            return "提取失败";
        }

        var content = metadata.Content;
        if (content is null)
        {
            return "已提取";
        }

        var parts = new List<string>();
        if (content.Width is not null && content.Height is not null)
        {
            parts.Add($"{content.Width}×{content.Height}");
        }

        if (content.DurationMilliseconds is not null)
        {
            parts.Add(FormatDuration(content.DurationMilliseconds.Value));
        }

        if (!string.IsNullOrWhiteSpace(content.VideoCodec))
        {
            parts.Add(content.VideoCodec);
        }
        else if (!string.IsNullOrWhiteSpace(content.AudioCodec))
        {
            parts.Add(content.AudioCodec);
        }

        return parts.Count == 0
            ? content.Kind switch
            {
                AssetMediaKind.Image => "图片",
                AssetMediaKind.Audio => "音频",
                AssetMediaKind.Video => "视频",
                _ => "已提取"
            }
            : string.Join(" · ", parts);
    }

    private static string FormatDuration(long milliseconds)
    {
        var duration = TimeSpan.FromMilliseconds(milliseconds);
        return duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours}:{duration.Minutes:00}:{duration.Seconds:00}"
            : $"{duration.Minutes}:{duration.Seconds:00}";
    }

    private static string FormatTotalDuration(long milliseconds)
    {
        if (milliseconds <= 0)
        {
            return "0:00";
        }

        var totalSeconds = milliseconds / 1_000;
        var totalHours = totalSeconds / 3_600;
        var minutes = totalSeconds % 3_600 / 60;
        var seconds = totalSeconds % 60;
        return totalHours > 0
            ? $"{totalHours:N0}:{minutes:00}:{seconds:00}"
            : $"{minutes}:{seconds:00}";
    }

    internal static string GetApplicationVersion()
    {
        var informationalVersion = typeof(MainForm).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        return string.IsNullOrWhiteSpace(informationalVersion)
            ? System.Windows.Forms.Application.ProductVersion
            : informationalVersion.Split('+', 2)[0];
    }

    internal static string FormatFileSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes;
        var unit = 0;

        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0
            ? $"{bytes:N0} {units[unit]}"
            : $"{value:N1} {units[unit]}";
    }

    private void ShowError(string title, Exception exception)
    {
        _runtimeLog.WriteError(title, exception);
        MessageBox.Show(
            this,
            exception.Message,
            title,
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }
}
