using CDSI.Agent.WinForms;
using CDSI.Agent.Application.Storage;
using CDSI.Agent.Core.Assets;
using CDSI.Agent.Core.Duplicates;
using CDSI.Agent.Core.Scanning;
using CDSI.Agent.Core.Storage;

namespace CDSI.Agent.WinForms.Tests;

public sealed class MainFormLayoutTests
{
    [Fact]
    public void ConfigureStartupWindow_DefaultsToMaximizedAndKeepsRestoreSize()
    {
        using var form = new Form();

        MainForm.ConfigureStartupWindow(form);

        Assert.Equal(FormWindowState.Maximized, form.WindowState);
        Assert.Equal(FormStartPosition.CenterScreen, form.StartPosition);
        Assert.Equal(new Size(920, 600), form.MinimumSize);
        Assert.Equal(new Size(1180, 760), form.Size);
    }

    [Fact]
    public void ConfigureMainMenuStrip_UsesTheExpectedTopLevelStructure()
    {
        using var menuStrip = new MenuStrip();
        ToolStripMenuItem[] menus =
        [
            new("文件"),
            new("扫描"),
            new("资产"),
            new("视图"),
            new("工具"),
            new("设置"),
            new("帮助")
        ];

        MainForm.ConfigureMainMenuStrip(menuStrip, menus);

        Assert.Equal(
            ["文件", "扫描", "资产", "视图", "工具", "设置", "帮助"],
            menuStrip.Items.Cast<ToolStripMenuItem>().Select(item => item.Text));
        Assert.Equal(DockStyle.Fill, menuStrip.Dock);
        Assert.Equal("主菜单", menuStrip.AccessibleName);
    }

    [Fact]
    public void ConfigureMainTabs_PlacesStatisticsLast()
    {
        using var tabControl = new TabControl();
        TabPage[] tabPages =
        [
            new("全部资产"),
            new("资产目录"),
            new("重复文件"),
            new("项目管理"),
            new("云备份管理"),
            new("Git项目管理"),
            new("统计")
        ];

        MainForm.ConfigureMainTabs(tabControl, tabPages);

        Assert.Equal(
            [
                "全部资产",
                "资产目录",
                "重复文件",
                "项目管理",
                "云备份管理",
                "Git项目管理",
                "统计"
            ],
            tabControl.TabPages.Cast<TabPage>().Select(page => page.Text));
        Assert.Equal(DockStyle.Fill, tabControl.Dock);
        Assert.Equal(new Point(12, 5), tabControl.Padding);
    }

    [Fact]
    public void ConfigureMainContentLayout_UsesNoDuplicateCommandRow()
    {
        using var mainLayout = new TableLayoutPanel
        {
            ColumnCount = 1,
            RowCount = 4
        };
        using var content = new Panel();
        using var progress = new Panel();
        var progressRowStyle = new RowStyle();

        MainForm.ConfigureMainContentLayout(
            mainLayout,
            content,
            progress,
            progressRowStyle);

        Assert.Equal(2, mainLayout.GetRow(content));
        Assert.Equal(3, mainLayout.GetRow(progress));
        Assert.Equal(4, mainLayout.RowStyles.Count);
        Assert.Equal(SizeType.AutoSize, mainLayout.RowStyles[0].SizeType);
        Assert.Equal(SizeType.AutoSize, mainLayout.RowStyles[1].SizeType);
        Assert.Equal(SizeType.Percent, mainLayout.RowStyles[2].SizeType);
        Assert.Equal(100, mainLayout.RowStyles[2].Height);
        Assert.Equal(SizeType.Absolute, mainLayout.RowStyles[3].SizeType);
        Assert.Equal(0, mainLayout.RowStyles[3].Height);
        Assert.False(progress.Visible);

        MainForm.SetProgressVisibility(progress, progressRowStyle, visible: true);

        Assert.Equal(58, mainLayout.RowStyles[3].Height);
        Assert.True(progress.Visible);
    }

    [Fact]
    public void CreateMainBanner_GrowsWhenDisplayScaledTextNeedsMoreHeight()
    {
        using var banner = MainForm.CreateMainBanner("0.2.10");
        banner.Size = new Size(900, 1);
        banner.CreateControl();
        banner.PerformLayout();
        var initialHeight = banner.GetPreferredSize(new Size(900, 0)).Height;
        var titleLabel = Assert.Single(
            banner.Controls.OfType<Label>(),
            label => label.AccessibleName == "应用名称");

        titleLabel.Font = new Font(
            titleLabel.Font.FontFamily,
            titleLabel.Font.Size * 2,
            titleLabel.Font.Style);
        banner.PerformLayout();
        var scaledHeight = banner.GetPreferredSize(new Size(900, 0)).Height;

        Assert.True(banner.AutoSize);
        Assert.Equal(AutoSizeMode.GrowAndShrink, banner.AutoSizeMode);
        Assert.All(
            banner.RowStyles.Cast<RowStyle>(),
            style => Assert.Equal(SizeType.AutoSize, style.SizeType));
        Assert.True(scaledHeight > initialHeight);
    }

    [Fact]
    public void ApplicationVersion_UsesThreeSegmentsWithATwoDigitRevision()
    {
        var version = MainForm.GetApplicationVersion();
        var parts = version.Split('.');

        Assert.Equal(3, parts.Length);
        Assert.All(parts, part => Assert.True(int.TryParse(part, out _)));
        Assert.Equal(2, parts[2].Length);
        Assert.InRange(int.Parse(parts[2]), 10, 99);
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    public void ShouldAllowTaskCancellation_DoesNotDependOnAToolbarButton(
        bool busy,
        bool allowCancel,
        bool expected)
    {
        Assert.Equal(
            expected,
            MainForm.ShouldAllowTaskCancellation(busy, allowCancel));
    }

    [Fact]
    public void GetDueIdleScanRoots_UsesTheLaterScanOrConfigurationTime()
    {
        var now = DateTimeOffset.Parse("2026-08-28T12:00:00+08:00");
        var due = CreateIdleScanRoot(
            now.AddMinutes(-20),
            now.AddMinutes(-15));
        var recentlyConfigured = CreateIdleScanRoot(
            now.AddMinutes(-20),
            now.AddMinutes(-5));
        var offline = CreateIdleScanRoot(
            now.AddMinutes(-20),
            now.AddMinutes(-15)) with
        {
            Status = ScanRootStatus.Offline
        };
        var scheduleDisabled = CreateIdleScanRoot(
            now.AddMinutes(-20),
            now.AddMinutes(-15)) with
        {
            IdleSchedule = IdleScanSchedule.Disabled
        };

        var result = MainForm.GetDueIdleScanRoots(
            [due, recentlyConfigured, offline, scheduleDisabled],
            now);

        Assert.Equal(due.Id, Assert.Single(result).Id);
    }

    [Theory]
    [InlineData(false, false, false, true)]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, true, false)]
    public void CanStartIdleScan_RequiresAnIdleApplication(
        bool isBusy,
        bool checkInProgress,
        bool hasOpenModalWindow,
        bool expected)
    {
        Assert.Equal(
            expected,
            MainForm.CanStartIdleScan(
                isBusy,
                checkInProgress,
                hasOpenModalWindow));
    }

    [Fact]
    public void ConfigureEscapeShortcutDisplay_DoesNotAssignAnInvalidShortcutKey()
    {
        using var menuItem = new ToolStripMenuItem();

        MainForm.ConfigureEscapeShortcutDisplay(menuItem);

        Assert.Equal(Keys.None, menuItem.ShortcutKeys);
        Assert.Equal("Esc", menuItem.ShortcutKeyDisplayString);
    }

    [Fact]
    public void ConfigureSettingsShortcutDisplay_UsesAReadableCommaLabel()
    {
        using var menuItem = new ToolStripMenuItem();

        MainForm.ConfigureSettingsShortcutDisplay(menuItem);

        Assert.Equal(Keys.Control | Keys.Oemcomma, menuItem.ShortcutKeys);
        Assert.Equal("Ctrl + 逗号键", menuItem.ShortcutKeyDisplayString);
    }

    [Theory]
    [InlineData((int)Keys.Escape, (int)MainForm.MainShortcutCommand.CancelCurrentTask)]
    [InlineData((int)(Keys.Control | Keys.F), (int)MainForm.MainShortcutCommand.FocusAssetFilter)]
    [InlineData((int)(Keys.Control | Keys.N), (int)MainForm.MainShortcutCommand.CreateProject)]
    [InlineData((int)(Keys.Control | Keys.A), (int)MainForm.MainShortcutCommand.SelectAllAssets)]
    [InlineData((int)(Keys.Alt | Keys.Enter), (int)MainForm.MainShortcutCommand.ShowAssetDetails)]
    [InlineData((int)(Keys.Shift | Keys.F10), (int)MainForm.MainShortcutCommand.ShowContextMenu)]
    [InlineData((int)(Keys.Control | Keys.Shift | Keys.Tab), (int)MainForm.MainShortcutCommand.PreviousTab)]
    [InlineData((int)(Keys.Control | Keys.Tab), (int)MainForm.MainShortcutCommand.NextTab)]
    [InlineData((int)Keys.Enter, (int)MainForm.MainShortcutCommand.LocateAsset)]
    [InlineData((int)Keys.Delete, (int)MainForm.MainShortcutCommand.DeleteSelection)]
    [InlineData((int)(Keys.Control | Keys.Alt | Keys.Enter), (int)MainForm.MainShortcutCommand.None)]
    public void ResolveMainShortcut_MapsOnlyTheSupportedExactCombination(
        int keyData,
        int expectedCommand)
    {
        Assert.Equal(
            (MainForm.MainShortcutCommand)expectedCommand,
            MainForm.ResolveMainShortcut((Keys)keyData));
    }

    [Theory]
    [InlineData(0, 5, true, 4)]
    [InlineData(4, 5, false, 0)]
    [InlineData(2, 5, true, 1)]
    [InlineData(2, 5, false, 3)]
    [InlineData(0, 0, false, -1)]
    public void GetAdjacentTabIndex_WrapsAtBothEnds(
        int currentIndex,
        int tabCount,
        bool previous,
        int expectedIndex)
    {
        Assert.Equal(
            expectedIndex,
            MainForm.GetAdjacentTabIndex(currentIndex, tabCount, previous));
    }

    [Fact]
    public void SelectAllGridRows_SelectsOnlyRowsInTheCurrentGrid()
    {
        using var grid = CreateSelectionGrid();
        grid.ClearSelection();

        var selected = MainForm.SelectAllGridRows(grid);

        Assert.True(selected);
        Assert.Equal(grid.Rows.Count, grid.SelectedRows.Count);
    }

    [Fact]
    public void AssetDetails_IncludeStableIdentityIntegrityAndLocationFields()
    {
        var assetId = Guid.Parse("6a85382d-fdfd-4533-ad6f-14333ad6f14a");
        var sha256 = new string('a', 64);
        var asset = new AssetListItem(
            assetId,
            "creator-video.mp4",
            ".mp4",
            "video/mp4",
            2048,
            sha256,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            Path.Combine(Path.GetTempPath(), "creator-video.mp4"),
            AssetLocationOwnership.External,
            AssetLocationStatus.Available,
            AssetStatus.Indexed,
            true)
        {
            Tags = ["素材", "成片"]
        };

        var entries = AssetDetailsForm.CreateDetailEntries(asset)
            .ToDictionary(entry => entry.Name, entry => entry.Value);

        Assert.Equal(assetId.ToString("D"), entries["资产 ID"]);
        Assert.Equal(sha256, entries["文件校验值（SHA-256）"]);
        Assert.Equal(asset.Path, entries["本地位置"]);
        Assert.Equal("素材、成片", entries["标签"]);
        Assert.Equal("已备份", entries["OSS 备份"]);
        Assert.Equal("可用", entries["位置状态"]);
    }

    [Fact]
    public void ConfigureDetailsGrid_KeepsValuesReadableAndCopyable()
    {
        using var grid = new DataGridView();

        AssetDetailsForm.ConfigureDetailsGrid(
            grid,
            [new AssetDetailEntry("本地位置", @"D:\Creator\video.mp4")]);

        Assert.True(grid.ReadOnly);
        Assert.True(grid.AllowUserToResizeColumns);
        Assert.Equal(DataGridViewAutoSizeRowsMode.AllCells, grid.AutoSizeRowsMode);
        Assert.Equal(DataGridViewClipboardCopyMode.EnableWithoutHeaderText, grid.ClipboardCopyMode);
        Assert.Equal("本地位置", grid.Rows[0].Cells["Property"].Value);
        Assert.Equal(@"D:\Creator\video.mp4", grid.Rows[0].Cells["Value"].Value);
    }

    [Theory]
    [InlineData(0x0007)]
    [InlineData(0x8000)]
    [InlineData(0x8004)]
    public void IsLocalVolumeDeviceChange_AcceptsRelevantWindowsEvents(
        int eventType)
    {
        Assert.True(MainForm.IsLocalVolumeDeviceChange(0x0219, eventType));
        Assert.False(MainForm.IsLocalVolumeDeviceChange(0x0200, eventType));
    }

    [Fact]
    public void IsLocalVolumeDeviceChange_RejectsUnrelatedDeviceEvents()
    {
        Assert.False(MainForm.IsLocalVolumeDeviceChange(0x0219, 0x8001));
    }

    [Fact]
    public void CreateStatisticsDashboard_GroupsMetricsInAResponsiveGrid()
    {
        Label[] values = [new(), new(), new(), new(), new(), new(), new()];
        using var dashboard = MainForm.CreateStatisticsDashboard(
            [
                new MainForm.StatisticsSection(
                    "资产构成",
                    [
                        new("资产总数", values[0]),
                        new("视频文件", values[1]),
                        new("音频文件", values[2]),
                        new("图片文件", values[3]),
                        new("文本 / 文档", values[4]),
                        new("其他类型", values[5])
                    ]),
                new MainForm.StatisticsSection(
                    "媒体",
                    [new("视频总时长", values[6])])
            ]);
        dashboard.Size = new Size(900, 480);
        dashboard.CreateControl();
        dashboard.PerformLayout();

        Assert.True(dashboard.AutoScroll);
        Assert.Equal(5, dashboard.RowCount);
        Assert.Equal(
            ["资产构成", "媒体"],
            dashboard.Controls
                .OfType<Label>()
                .Select(label => label.Text));
        Assert.All(values, value =>
        {
            Assert.Equal("0", value.Text);
            Assert.False(string.IsNullOrWhiteSpace(value.AccessibleName));
            Assert.Equal(ContentAlignment.MiddleLeft, value.TextAlign);
        });
        Assert.All(
            dashboard.Controls.OfType<TableLayoutPanel>(),
            metricGrid => Assert.Equal(3, metricGrid.ColumnCount));
    }

    [Fact]
    public void CreateStatisticsDashboard_PlacesTheAssetPieChartBesideMetrics()
    {
        using var chart = new AssetCompositionPieChart();
        Label[] values = [new(), new(), new(), new(), new(), new()];
        using var dashboard = MainForm.CreateStatisticsDashboard(
            [
                new MainForm.StatisticsSection(
                    "资产构成",
                    [
                        new("资产总数", values[0]),
                        new("视频文件", values[1]),
                        new("音频文件", values[2]),
                        new("图片文件", values[3]),
                        new("文本 / 文档", values[4]),
                        new("其他类型", values[5])
                    ])
            ],
            chart);

        var compositionLayout = Assert.Single(
            dashboard.Controls.OfType<TableLayoutPanel>(),
            control => control.AccessibleName == "资产构成统计");
        Assert.Equal(2, compositionLayout.ColumnCount);
        Assert.Same(chart, compositionLayout.GetControlFromPosition(0, 0));
        Assert.IsType<TableLayoutPanel>(
            compositionLayout.GetControlFromPosition(1, 0));
        Assert.Equal(236, dashboard.RowStyles[1].Height);
    }

    [Fact]
    public void AssetCompositionPieChart_StoresAndRendersTheTypeBreakdown()
    {
        using var chart = new AssetCompositionPieChart
        {
            Size = new Size(420, 236)
        };
        chart.SetValues(
            totalAssetCount: 20,
            videoCount: 8,
            audioCount: 4,
            imageCount: 3,
            documentCount: 2,
            otherCount: 3);

        Assert.Equal(20, chart.TotalAssetCount);
        Assert.Equal(
            ["视频", "音频", "图片", "文本 / 文档", "其他"],
            chart.Slices.Select(slice => slice.Name));
        Assert.Equal([8L, 4L, 3L, 2L, 3L],
            chart.Slices.Select(slice => slice.Count));
        Assert.Contains("资产总数 20", chart.AccessibleDescription);
        Assert.Contains("视频 8", chart.AccessibleDescription);

        using var bitmap = new Bitmap(chart.Width, chart.Height);
        chart.DrawToBitmap(bitmap, chart.ClientRectangle);
        var sampledColors = new HashSet<Color>();
        for (var x = 0; x < bitmap.Width; x += 12)
        {
            for (var y = 0; y < bitmap.Height; y += 12)
            {
                sampledColors.Add(bitmap.GetPixel(x, y));
            }
        }

        Assert.True(sampledColors.Count >= 6);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            chart.SetValues(-1, 0, 0, 0, 0, 0));
    }

    [Fact]
    public void ProjectManagementGrids_AllowEveryColumnWidthToBeAdjusted()
    {
        using var projectGrid = new DataGridView();
        using var memberGrid = new DataGridView();

        MainForm.ConfigureProjectManagementGridColumns(projectGrid, memberGrid);

        Assert.Equal(
            ["名称", "类型", "云端备份", "创建时间", "资产", "大小", "已备份"],
            projectGrid.Columns
                .Cast<DataGridViewColumn>()
                .Select(column => column.HeaderText));
        Assert.Equal(
            ["资源ID", "文件", "类型", "大小", "加入时间", "位置", "备份状态"],
            memberGrid.Columns
                .Cast<DataGridViewColumn>()
                .Select(column => column.HeaderText));
        Assert.Equal(
            "ProjectAssetId",
            memberGrid.Columns
                .Cast<DataGridViewColumn>()
                .Single(column => column.HeaderText == "资源ID")
                .Name);
        Assert.Equal(
            "备份状态",
            memberGrid.Columns["BackupStatus"]?.HeaderText);
        Assert.All(
            projectGrid.Columns.Cast<DataGridViewColumn>()
                .Concat(memberGrid.Columns.Cast<DataGridViewColumn>()),
            column =>
            {
                Assert.Equal(DataGridViewAutoSizeColumnMode.None, column.AutoSizeMode);
                Assert.Equal(DataGridViewTriState.True, column.Resizable);
            });
        Assert.Equal(ScrollBars.Both, projectGrid.ScrollBars);
        Assert.Equal(ScrollBars.Both, memberGrid.ScrollBars);
        Assert.True(projectGrid.MultiSelect);
        Assert.True(memberGrid.MultiSelect);
        Assert.Equal(
            DataGridViewSelectionMode.FullRowSelect,
            projectGrid.SelectionMode);
        Assert.Equal(
            DataGridViewSelectionMode.FullRowSelect,
            memberGrid.SelectionMode);
    }

    [Fact]
    public void CloudBackupManagementGrids_UseProjectAndBackupColumns()
    {
        using var projectGrid = new DataGridView();
        using var backupGrid = new DataGridView();

        MainForm.ConfigureCloudBackupManagementGridColumns(
            projectGrid,
            backupGrid);

        Assert.Equal(
            [
                "项目", "云提供商", "项目资源数", "云端副本",
                "占用空间", "最近备份"
            ],
            projectGrid.Columns
                .Cast<DataGridViewColumn>()
                .Select(column => column.HeaderText));
        Assert.Equal(
            [
                "本地资产", "所属项目", "云端文件名", "对象键",
                "大小", "校验状态", "备份时间"
            ],
            backupGrid.Columns
                .Cast<DataGridViewColumn>()
                .Select(column => column.HeaderText));
        Assert.All(
            projectGrid.Columns.Cast<DataGridViewColumn>()
                .Concat(backupGrid.Columns.Cast<DataGridViewColumn>()),
            column =>
            {
                Assert.Equal(DataGridViewAutoSizeColumnMode.None, column.AutoSizeMode);
                Assert.Equal(DataGridViewTriState.True, column.Resizable);
            });
    }

    [Fact]
    public void CloudBackupManagementLayout_UsesProjectAndBackupPanes()
    {
        using var projectGrid = new DataGridView();
        using var backupGrid = new DataGridView();
        using var split = MainForm.CreateCloudBackupProjectSplit(
            projectGrid,
            backupGrid);

        Assert.Equal(Orientation.Vertical, split.Orientation);
        Assert.Contains(projectGrid, Descendants(split.Panel1));
        Assert.Contains(backupGrid, Descendants(split.Panel2));
        Assert.Contains(
            Descendants(split.Panel1).OfType<Label>(),
            label => label.Text == "备份项目");
        Assert.Contains(
            Descendants(split.Panel2).OfType<Label>(),
            label => label.Text == "云端备份");
    }

    [Fact]
    public void GroupCloudBackupsByProject_HandlesMultipleProjectsAndUnassignedBackups()
    {
        var firstAssetId = Guid.NewGuid();
        var secondAssetId = Guid.NewGuid();
        var backups = new[]
        {
            CreateCloudBackup(
                firstAssetId,
                10,
                "项目甲/原片.mov",
                ["项目甲", "项目乙"],
                ObjectStorageProvider.AliyunOss),
            CreateCloudBackup(
                firstAssetId,
                12,
                "项目甲/代理文件.mov",
                ["项目甲", "项目乙"],
                ObjectStorageProvider.TencentCos),
            CreateCloudBackup(
                secondAssetId,
                20,
                "项目乙/成片.mov",
                ["项目甲", "项目乙"],
                ObjectStorageProvider.QiniuKodo),
            CreateCloudBackup(Guid.NewGuid(), 30, $"assets/{Guid.NewGuid():N}/旧版.mov", [])
        };

        var groups = MainForm.GroupCloudBackupsByProject(backups);

        Assert.Equal(["项目甲", "项目乙", "未归属项目"],
            groups.Select(group => group.Name));
        var primary = groups[0];
        Assert.False(primary.IsUnassigned);
        Assert.Equal(1, primary.AssetCount);
        Assert.Equal(2, primary.Backups.Count);
        Assert.Equal(22, primary.TotalSizeBytes);
        Assert.Equal(
            "阿里云 OSS、腾讯云 COS",
            MainForm.FormatCloudBackupProjectProviders(primary));
        Assert.Single(groups[1].Backups);
        Assert.Equal(secondAssetId, groups[1].Backups[0].Source.AssetId);
        Assert.True(groups[2].IsUnassigned);
        Assert.Equal(30, groups[2].TotalSizeBytes);
        Assert.Equal("项目甲", MainForm.GetCloudBackupProjectName("项目甲/原片.mov"));
        Assert.Null(MainForm.GetCloudBackupProjectName("asset.mov"));
        Assert.Null(MainForm.GetCloudBackupProjectName("assets/id/asset.mov"));
    }

    [Fact]
    public void CreateAssetTabLayout_GivesTheRemovedStatisticsSpaceToTheGrid()
    {
        using var filterPanel = new Panel();
        using var assetGrid = new DataGridView();
        using var paginationPanel = new Panel();
        using var detailsPanel = new Panel();
        using var layout = MainForm.CreateAssetTabLayout(
            filterPanel,
            assetGrid,
            paginationPanel,
            detailsPanel);

        Assert.Equal(4, layout.RowCount);
        Assert.Equal(0, layout.GetRow(filterPanel));
        Assert.Equal(1, layout.GetRow(assetGrid));
        Assert.Equal(2, layout.GetRow(paginationPanel));
        Assert.Equal(3, layout.GetRow(detailsPanel));
        Assert.Equal(SizeType.Percent, layout.RowStyles[1].SizeType);
        Assert.Equal(100, layout.RowStyles[1].Height);
        Assert.Equal(64, layout.RowStyles[3].Height);
    }

    [Fact]
    public void CreateAssetDetailsPanel_UsesTheFullAvailableWidth()
    {
        using var titleLabel = new Label();
        using var summaryLabel = new Label();
        using var panel = MainForm.CreateAssetDetailsPanel(
            titleLabel,
            summaryLabel);
        panel.Size = new Size(900, 64);
        panel.CreateControl();
        panel.PerformLayout();

        Assert.Single(panel.ColumnStyles);
        Assert.Equal(SizeType.Percent, panel.ColumnStyles[0].SizeType);
        Assert.Equal(2, panel.Controls.Count);
        Assert.Equal(2, panel.RowCount);
        Assert.Same(titleLabel, panel.GetControlFromPosition(0, 0));
        Assert.Same(summaryLabel, panel.GetControlFromPosition(0, 1));
        Assert.True(titleLabel.Width > 800);
        Assert.True(summaryLabel.Width > 800);
        Assert.True(titleLabel.AutoEllipsis);
        Assert.True(summaryLabel.AutoEllipsis);
        Assert.DoesNotContain(
            panel.Controls.Cast<Control>(),
            control => control is TextBox);
    }
    [Fact]
    public void CreateAssetPaginationPanel_OffersSupportedPageSizes()
    {
        using var pageSizeComboBox = new ComboBox();
        using var previousButton = new Button();
        using var pageLabel = new Label();
        using var nextButton = new Button();
        using var panel = MainForm.CreateAssetPaginationPanel(
            pageSizeComboBox,
            previousButton,
            pageLabel,
            nextButton);
        panel.Size = new Size(900, 36);
        panel.CreateControl();
        panel.PerformLayout();

        Assert.Equal(
            [100, 200, 500],
            pageSizeComboBox.Items.Cast<int>());
        Assert.Equal(ComboBoxStyle.DropDownList, pageSizeComboBox.DropDownStyle);
        Assert.Equal(100, pageSizeComboBox.SelectedItem);
        Assert.Equal("上一页", previousButton.Text);
        Assert.Equal("下一页", nextButton.Text);
        Assert.Equal("第 1 / 1 页 · 0 条", pageLabel.Text);
    }

    [Fact]
    public void CreateAssetFilterPanel_ContainsTypeDatesAndExplicitCommands()
    {
        using var filenameTextBox = new TextBox();
        using var fileTypeComboBox = new ComboBox();
        using var extensionComboBox = new ComboBox();
        using var tagComboBox = new ComboBox();
        using var createdFromDatePicker = new DateTimePicker();
        using var createdToDatePicker = new DateTimePicker();
        using var applyButton = new Button { Text = "搜索" };
        using var resetButton = new Button { Text = "重置" };
        using var resultLabel = new Label();
        using var panel = MainForm.CreateAssetFilterPanel(
            filenameTextBox,
            fileTypeComboBox,
            extensionComboBox,
            tagComboBox,
            createdFromDatePicker,
            createdToDatePicker,
            applyButton,
            resetButton,
            resultLabel);
        panel.Size = new Size(920, 82);
        panel.CreateControl();
        panel.PerformLayout();

        Assert.Contains(filenameTextBox, panel.Controls.Cast<Control>());
        Assert.Contains(fileTypeComboBox, panel.Controls.Cast<Control>());
        Assert.Contains(extensionComboBox, panel.Controls.Cast<Control>());
        Assert.Contains(tagComboBox, panel.Controls.Cast<Control>());
        Assert.Contains(createdFromDatePicker, panel.Controls.Cast<Control>());
        Assert.Contains(createdToDatePicker, panel.Controls.Cast<Control>());
        Assert.Contains(applyButton, panel.Controls.Cast<Control>());
        Assert.Contains(resetButton, panel.Controls.Cast<Control>());
        Assert.Contains(resultLabel, panel.Controls.Cast<Control>());
        Assert.True(Assert.IsType<FlowLayoutPanel>(panel).WrapContents);
        Assert.All(
            panel.Controls.Cast<Control>(),
            control => Assert.True(
                control.Bottom <= panel.ClientSize.Height,
                $"{control.GetType().Name} '{control.Text}' 超出搜索区域。"));
        Assert.Equal("搜索", applyButton.Text);
        Assert.Equal(
            Enum.GetValues<CDSI.Agent.Core.Assets.AssetFileTypeFilter>(),
            MainForm.AssetFileTypeFilterChoices.Select(choice => choice.Value));
    }

    [Fact]
    public void BuildAssetListFilter_TreatsTheEndDateAsInclusive()
    {
        var from = new DateTime(2026, 8, 1);
        var to = new DateTime(2026, 8, 20);
        var tagId = Guid.NewGuid();

        var filter = MainForm.BuildAssetListFilter(
            CDSI.Agent.Core.Assets.AssetFileTypeFilter.Video,
            createdFromEnabled: true,
            from,
            createdToEnabled: true,
            to,
            extension: "MP4",
            tagId: tagId,
            filenameContains: "  Final Cut  ");

        Assert.Equal(
            from,
            filter.CreatedFrom?.ToLocalTime().Date);
        Assert.Equal(
            to.AddDays(1),
            filter.CreatedBefore?.ToLocalTime().Date);
        Assert.Equal(
            CDSI.Agent.Core.Assets.AssetFileTypeFilter.Video,
            filter.FileType);
        Assert.Equal(".mp4", filter.Extension);
        Assert.Equal(tagId, filter.TagId);
        Assert.Equal("Final Cut", filter.FilenameContains);
        Assert.Throws<ArgumentException>(() => MainForm.BuildAssetListFilter(
            CDSI.Agent.Core.Assets.AssetFileTypeFilter.All,
            createdFromEnabled: true,
            to,
            createdToEnabled: true,
            from));
    }

    [Fact]
    public void RefreshAssetExtensionChoices_SortsAndPreservesTheSelection()
    {
        using var comboBox = new ComboBox();
        comboBox.Items.AddRange([MainForm.AllAssetExtensionsLabel, ".mp4"]);
        comboBox.SelectedItem = ".mp4";

        MainForm.RefreshAssetExtensionChoices(
            comboBox,
            [".ZIP", ".jpg", ".zip"]);

        Assert.Equal(
            [MainForm.AllAssetExtensionsLabel, ".jpg", ".mp4", ".zip"],
            comboBox.Items.Cast<string>());
        Assert.Equal(".mp4", comboBox.SelectedItem);
    }

    [Fact]
    public void RefreshAssetTagChoices_SortsAndPreservesTheSelection()
    {
        var articleId = Guid.NewGuid();
        using var comboBox = new ComboBox();

        MainForm.RefreshAssetTagChoices(
            comboBox,
            [
                new(articleId, "文章", 3),
                new(Guid.NewGuid(), "素材", 5)
            ],
            articleId);

        var choices = comboBox.Items
            .Cast<MainForm.AssetTagFilterChoice>()
            .ToArray();
        Assert.Equal(MainForm.AllAssetTagsLabel, choices[0].Name);
        Assert.Equal(articleId, Assert.IsType<MainForm.AssetTagFilterChoice>(
            comboBox.SelectedItem).TagId);
        Assert.Equal("文章 (3)", comboBox.SelectedItem?.ToString());
    }

    [Fact]
    public void RefreshAssetExtensionChoices_ResetsAnIncompatibleSelection()
    {
        using var comboBox = new ComboBox();
        comboBox.Items.AddRange([MainForm.AllAssetExtensionsLabel, ".mp4"]);
        comboBox.SelectedItem = ".mp4";

        MainForm.RefreshAssetExtensionChoices(
            comboBox,
            [".png", ".jpg"],
            includeUnavailableSelection: false);

        Assert.Equal(
            [MainForm.AllAssetExtensionsLabel, ".jpg", ".png"],
            comboBox.Items.Cast<string>());
        Assert.Equal(MainForm.AllAssetExtensionsLabel, comboBox.SelectedItem);
    }

    [Fact]
    public void CalculateAssetPagination_ClampsToTheLastAvailablePage()
    {
        var state = MainForm.CalculateAssetPagination(
            totalItems: 250,
            pageSize: 100,
            requestedPageIndex: 99);

        Assert.Equal(2, state.PageIndex);
        Assert.Equal(3, state.PageCount);
        Assert.Equal(200, state.Offset);
        Assert.Equal(201, state.FirstItem);
        Assert.Equal(250, state.LastItem);
    }

    [Fact]
    public void CalculateAssetPagination_RepresentsAnEmptyListAsPageOne()
    {
        var state = MainForm.CalculateAssetPagination(
            totalItems: 0,
            pageSize: 200,
            requestedPageIndex: 5);

        Assert.Equal(0, state.PageIndex);
        Assert.Equal(1, state.PageCount);
        Assert.Equal(0, state.Offset);
        Assert.Equal(0, state.FirstItem);
        Assert.Equal(0, state.LastItem);
    }

    [Fact]
    public void EnableAssetMultiSelection_AllowsFullRowBatchSelection()
    {
        using var grid = new DataGridView
        {
            MultiSelect = false,
            SelectionMode = DataGridViewSelectionMode.CellSelect
        };

        MainForm.EnableAssetMultiSelection(grid);

        Assert.True(grid.MultiSelect);
        Assert.Equal(
            DataGridViewSelectionMode.FullRowSelect,
            grid.SelectionMode);
    }

    [Fact]
    public void EnableFreeColumnResizing_MakesEveryAssetColumnIndependent()
    {
        using var grid = new DataGridView
        {
            AllowUserToResizeColumns = false,
            ScrollBars = ScrollBars.Vertical
        };
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "文件",
            Width = 220,
            MinimumWidth = 160,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            Resizable = DataGridViewTriState.False
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "状态",
            Width = 80,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None
        });

        MainForm.EnableFreeColumnResizing(grid);

        Assert.True(grid.AllowUserToResizeColumns);
        Assert.Equal(DataGridViewAutoSizeColumnsMode.None, grid.AutoSizeColumnsMode);
        Assert.Equal(ScrollBars.Both, grid.ScrollBars);
        Assert.All(grid.Columns.Cast<DataGridViewColumn>(), column =>
        {
            Assert.Equal(DataGridViewAutoSizeColumnMode.None, column.AutoSizeMode);
            Assert.Equal(40, column.MinimumWidth);
            Assert.Equal(DataGridViewTriState.True, column.Resizable);
        });
        Assert.Equal(220, grid.Columns[0].Width);
        Assert.Equal(80, grid.Columns[1].Width);

        grid.Columns[0].Width = 360;
        grid.Columns[1].Width = 140;
        MainForm.ResetGridColumnWidths(grid);

        Assert.Equal(220, grid.Columns[0].Width);
        Assert.Equal(80, grid.Columns[1].Width);
    }

    [Fact]
    public void CreateOpenDocumentStartInfo_UsesTheRegisteredWindowsApplication()
    {
        var documentPath = Path.Combine(Path.GetTempPath(), "CDSI Docs", "README.md");

        var startInfo = MainForm.CreateOpenDocumentStartInfo(documentPath);

        Assert.Equal(Path.GetFullPath(documentPath), startInfo.FileName);
        Assert.True(startInfo.UseShellExecute);
    }

    [Fact]
    public void TaskCenterForm_AppliesDeterminateAndIndeterminateSnapshots()
    {
        var initial = new TaskCenterSnapshot(
            "正在扫描",
            "已索引 12",
            @"D:\Assets\clip.mp4",
            42,
            false,
            true,
            "数据目录: test");
        using var form = new TaskCenterForm(() => initial, () => { });

        form.ApplySnapshot(initial);

        Assert.Equal("正在扫描", form.StatusText);
        Assert.Equal("已索引 12", form.ProgressText);
        Assert.True(form.CanCancel);
        Assert.Equal(ProgressBarStyle.Continuous, form.CurrentProgressStyle);
        Assert.Equal(42, form.CurrentProgressValue);

        form.ApplySnapshot(initial with
        {
            ProgressPercent = null,
            IsIndeterminate = true,
            CanCancel = false
        });

        Assert.False(form.CanCancel);
        Assert.Equal(ProgressBarStyle.Marquee, form.CurrentProgressStyle);
    }

    [Fact]
    public void OpenFileLocationStartInfo_UsesExplorerWithStructuredArguments()
    {
        var filePath = Path.Combine(Path.GetTempPath(), "Creator Assets", "clip.mp4");

        var startInfo = MainForm.CreateOpenFileLocationStartInfo(filePath);

        Assert.Equal("explorer.exe", startInfo.FileName);
        Assert.True(startInfo.UseShellExecute);
        Assert.Equal(
            ["/select,", Path.GetFullPath(filePath)],
            startInfo.ArgumentList.ToArray());
    }

    [Fact]
    public void CloudBackupContextMenu_OffersOpenFileLocationBeforeCloudActions()
    {
        using var contextMenu = new ContextMenuStrip();
        using var openItem = new ToolStripMenuItem();
        using var restoreItem = new ToolStripMenuItem();
        using var deleteItem = new ToolStripMenuItem();

        MainForm.ConfigureCloudBackupContextMenu(
            contextMenu,
            openItem,
            restoreItem,
            deleteItem);

        Assert.Equal(5, contextMenu.Items.Count);
        Assert.Same(openItem, contextMenu.Items[0]);
        Assert.Equal("打开文件位置", openItem.Text);
        Assert.IsType<ToolStripSeparator>(contextMenu.Items[1]);
        Assert.Equal("取回", restoreItem.Text);
        Assert.IsType<ToolStripSeparator>(contextMenu.Items[3]);
        Assert.Equal("删除备份", deleteItem.Text);
    }

    [Fact]
    public void CloudBackupProjectContextMenu_OffersProjectBackupDeletion()
    {
        using var contextMenu = new ContextMenuStrip();
        using var deleteItem = new ToolStripMenuItem();

        MainForm.ConfigureCloudBackupProjectContextMenu(
            contextMenu,
            deleteItem);

        Assert.Single(contextMenu.Items);
        Assert.Same(deleteItem, contextMenu.Items[0]);
        Assert.Equal("删除备份项目", deleteItem.Text);
    }

    [Fact]
    public void CloudBackupProjectDeletionConfirmation_PreservesLocalData()
    {
        var backup = CreateCloudBackup(
            Guid.NewGuid(),
            42,
            "纪录片/成片.mov",
            ["纪录片"],
            ObjectStorageProvider.TencentCos);
        var project = Assert.Single(
            MainForm.GroupCloudBackupsByProject([backup]));

        var confirmation =
            MainForm.CreateCloudBackupProjectDeletionConfirmation(project);

        Assert.Contains("备份项目“纪录片”", confirmation);
        Assert.Contains("1 个云端备份", confirmation);
        Assert.Contains("腾讯云 COS", confirmation);
        Assert.Contains("本地项目、项目成员关系和本地文件不会删除", confirmation);
        Assert.Contains("无法撤销", confirmation);
    }

    [Fact]
    public void FilterCloudBackups_MatchesAssetCloudProfileProviderAndLocalPath()
    {
        var now = DateTimeOffset.UtcNow;
        var assetId = Guid.NewGuid();
        var location = new ObjectStorageLocation(
            Guid.NewGuid(),
            assetId,
            Guid.NewGuid(),
            "纪录片/成片.MP4",
            StorageVerificationStatus.Healthy,
            42,
            new string('a', 64),
            "etag",
            now,
            now,
            now);
        var profile = new ObjectStorageProfile(
            location.StorageProfileId,
            "异地备份",
            ObjectStorageProvider.TencentCos,
            "cos.ap-guangzhou.myqcloud.com",
            "creator-1250000000",
            "ap-guangzhou",
            true,
            "secret-id",
            now,
            now);
        var source = new ObjectStorageRestoreSource(
            assetId,
            "原片.mp4",
            42,
            now,
            new string('a', 64),
            location,
            @"D:\Creator Assets\原片.mp4")
        {
            ProjectNames = ["纪录片项目"]
        };
        var backup = new ManagedObjectStorageBackup(source, profile, true);

        Assert.Single(MainForm.FilterCloudBackups([backup], "原片"));
        Assert.Single(MainForm.FilterCloudBackups([backup], "成片.mp4"));
        Assert.Single(MainForm.FilterCloudBackups([backup], "纪录片/"));
        Assert.Single(MainForm.FilterCloudBackups([backup], "异地备份"));
        Assert.Single(MainForm.FilterCloudBackups([backup], "腾讯"));
        Assert.Single(MainForm.FilterCloudBackups([backup], "creator assets"));
        Assert.Single(MainForm.FilterCloudBackups([backup], "纪录片项目"));
        Assert.Single(MainForm.FilterCloudBackups([backup], "  "));
        Assert.Empty(MainForm.FilterCloudBackups([backup], "不存在"));
    }

    [Fact]
    public void GetCloudBackupLocalPath_UsesThePathBoundToTheSelectedRow()
    {
        var localPath = Path.Combine(Path.GetTempPath(), "Creator Assets", "clip.mp4");
        var location = new ObjectStorageLocation(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "项目一/clip.mp4",
            StorageVerificationStatus.Healthy,
            42,
            new string('a', 64),
            "etag",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);
        var source = new ObjectStorageRestoreSource(
            location.AssetId,
            "clip.mp4",
            42,
            DateTimeOffset.UtcNow,
            new string('a', 64),
            location,
            localPath);
        using var row = new DataGridViewRow
        {
            Tag = new ManagedObjectStorageBackup(source, null, false)
        };

        Assert.Equal(localPath, MainForm.GetCloudBackupLocalPath(row));
        Assert.Null(MainForm.GetCloudBackupLocalPath(null));
    }

    [Fact]
    public void GetDuplicateFilePath_UsesTheAssetBoundToTheSelectedRow()
    {
        var filePath = Path.Combine(Path.GetTempPath(), "Creator Assets", "copy.mp4");
        using var row = new DataGridViewRow
        {
            Tag = new DuplicateAssetItem(
                Guid.NewGuid(),
                "copy.mp4",
                filePath,
                DateTimeOffset.UtcNow,
                AssetLocationStatus.Available)
        };

        Assert.Equal(filePath, MainForm.GetDuplicateFilePath(row));
        Assert.Null(MainForm.GetDuplicateFilePath(null));
    }

    [Fact]
    public void CreateAssetListRemovalConfirmation_StatesThatFilesAreNotDeleted()
    {
        var asset = new AssetListItem(
            Guid.NewGuid(),
            "creator-video.mp4",
            ".mp4",
            "video/mp4",
            1024,
            null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            Path.Combine(Path.GetTempPath(), "creator-video.mp4"),
            AssetLocationOwnership.External,
            AssetLocationStatus.Available,
            AssetStatus.Indexed,
            false);

        var message = MainForm.CreateAssetListRemovalConfirmation([asset]);

        Assert.Contains("creator-video.mp4", message);
        Assert.Contains("本地文件", message);
        Assert.Contains("不会被删除", message);
        Assert.Throws<ArgumentException>(() =>
            MainForm.CreateAssetListRemovalConfirmation([]));
    }

    [Fact]
    public void RightClickSelection_WithShift_SelectsTheAnchorRange()
    {
        using var grid = CreateSelectionGrid();
        grid.CurrentCell = grid.Rows[1].Cells[0];
        grid.ClearSelection();
        grid.Rows[1].Selected = true;

        MainForm.ApplyAssetGridRightClickSelection(
            grid,
            rowIndex: 4,
            columnIndex: 0,
            Keys.Shift);

        var selectedIndexes = grid.SelectedRows
            .Cast<DataGridViewRow>()
            .Select(row => row.Index)
            .Order()
            .ToArray();
        Assert.Equal([1, 2, 3, 4], selectedIndexes);
        Assert.Equal(4, grid.CurrentCell.RowIndex);
    }

    [Fact]
    public void RightClickSelection_PreservesBatchAndControlAddsARow()
    {
        using var grid = CreateSelectionGrid();
        grid.CurrentCell = grid.Rows[1].Cells[0];
        grid.ClearSelection();
        grid.Rows[1].Selected = true;
        grid.Rows[3].Selected = true;

        MainForm.ApplyAssetGridRightClickSelection(grid, 3, 0, Keys.None);
        Assert.Equal(
            [1, 3],
            grid.SelectedRows
                .Cast<DataGridViewRow>()
                .Select(row => row.Index)
                .Order());

        MainForm.ApplyAssetGridRightClickSelection(grid, 4, 0, Keys.Control);
        Assert.Equal(
            [1, 3, 4],
            grid.SelectedRows
                .Cast<DataGridViewRow>()
                .Select(row => row.Index)
                .Order());
    }

    private static DataGridView CreateSelectionGrid()
    {
        var grid = new DataGridView
        {
            AllowUserToAddRows = false,
            MultiSelect = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect
        };
        grid.Columns.Add("Name", "Name");
        for (var index = 0; index < 5; index++)
        {
            grid.Rows.Add($"Asset {index}");
        }

        return grid;
    }

    [Fact]
    public void FileSizeColumn_SortsByRawBytesAcrossDisplayUnits()
    {
        using var grid = new DataGridView
        {
            AllowUserToAddRows = false
        };
        var column = MainForm.CreateFileSizeColumn();
        grid.Columns.Add(column);
        long[] sizes =
        [
            2L * 1024 * 1024 * 1024,
            950L * 1024 * 1024,
            10L * 1024,
            12L * 1024 * 1024 * 1024
        ];
        foreach (var size in sizes)
        {
            grid.Rows.Add(size);
        }

        grid.Sort(column, System.ComponentModel.ListSortDirection.Ascending);
        var ascending = grid.Rows
            .Cast<DataGridViewRow>()
            .Select(row => Assert.IsType<long>(row.Cells[0].Value))
            .ToArray();
        grid.Sort(column, System.ComponentModel.ListSortDirection.Descending);
        var descending = grid.Rows
            .Cast<DataGridViewRow>()
            .Select(row => Assert.IsType<long>(row.Cells[0].Value))
            .ToArray();

        Assert.Equal(typeof(long), column.ValueType);
        Assert.Equal(sizes.Order().ToArray(), ascending);
        Assert.Equal(sizes.OrderDescending().ToArray(), descending);
    }

    [Fact]
    public void AssetIdColumn_DisplaysTheFinalHexSegment()
    {
        var assetId = Guid.Parse("6a85382d-fdfd-4533-ad6f-14333ad6f14a");
        using var grid = new DataGridView
        {
            AllowUserToAddRows = false
        };
        var column = MainForm.CreateAssetIdColumn();
        grid.Columns.Add(column);
        grid.Rows.Add(MainForm.FormatAssetIdForList(assetId));

        Assert.Equal("AssetId", column.Name);
        Assert.Equal("资产 ID", column.HeaderText);
        Assert.Equal(typeof(string), column.ValueType);
        Assert.Equal("14333ad6f14a", grid.Rows[0].Cells[0].Value);
        Assert.Equal(118, column.Width);
    }

    [Theory]
    [MemberData(nameof(AssetProjectDisplayCases))]
    public void AssetProjects_DisplayNamesOrNone(
        IReadOnlyList<string> projectNames,
        string expected)
    {
        Assert.Equal(expected, MainForm.FormatAssetProjects(projectNames));
    }

    public static TheoryData<IReadOnlyList<string>, string> AssetProjectDisplayCases =>
        new()
        {
            { Array.Empty<string>(), "无" },
            { new[] { "项目甲", "项目乙" }, "项目甲、项目乙" }
        };

    [Fact]
    public void Sha256Column_DisplaysTheCompleteFileChecksum()
    {
        var sha256 = new string('a', 64);
        using var grid = new DataGridView
        {
            AllowUserToAddRows = false
        };
        var column = MainForm.CreateSha256Column();
        grid.Columns.Add(column);
        grid.Rows.Add(sha256);

        Assert.Equal("Sha256", column.Name);
        Assert.Equal("文件校验值（SHA256）", column.HeaderText);
        Assert.Equal(typeof(string), column.ValueType);
        Assert.Equal(500, column.Width);
        Assert.Equal(sha256, grid.Rows[0].Cells[0].Value);
    }

    [Theory]
    [InlineData(null, "-")]
    [InlineData("", "-")]
    [InlineData("  ", "-")]
    [InlineData("0123456789abcdef", "0123456789abcdef")]
    public void Sha256ListValue_UsesDashWhenChecksumIsUnavailable(
        string? sha256,
        string expected)
    {
        Assert.Equal(expected, MainForm.FormatSha256ForList(sha256));
    }

    [Fact]
    public void AssetGridColumns_IncludeIndexTimeAndExcludeExtractedText()
    {
        using var grid = new DataGridView();

        MainForm.ConfigureAssetGridColumns(grid);

        var headers = grid.Columns
            .Cast<DataGridViewColumn>()
            .Select(column => column.HeaderText)
            .ToArray();
        Assert.Contains("索引时间", headers);
        Assert.Contains("文件校验值（SHA256）", headers);
        Assert.Contains("备份状态", headers);
        Assert.Contains("备份时间", headers);
        Assert.Contains("标签", headers);
        Assert.Contains("所属项目", headers);
        Assert.DoesNotContain("文本", headers);
        Assert.Equal("IndexedAt", grid.Columns["IndexedAt"]?.Name);
        Assert.Equal("Sha256", grid.Columns["Sha256"]?.Name);
        Assert.Equal("Projects", grid.Columns["Projects"]?.Name);
        Assert.Equal(
            "备份状态",
            grid.Columns["BackupStatus"]?.HeaderText);
        Assert.Equal("备份时间", grid.Columns["BackupTime"]?.HeaderText);
        Assert.True(grid.AllowUserToOrderColumns);
        Assert.True(
            grid.Columns["Projects"]!.DisplayIndex <
            grid.Columns.Cast<DataGridViewColumn>()
                .Single(column => column.HeaderText == "标签").DisplayIndex);
        Assert.Equal(
            grid.Columns["Projects"]!.DisplayIndex + 1,
            grid.Columns["BackupStatus"]!.DisplayIndex);
        Assert.Equal(
            grid.Columns["BackupStatus"]!.DisplayIndex + 1,
            grid.Columns["BackupTime"]!.DisplayIndex);
    }

    [Fact]
    public void RowNumberColumn_UsesTheVisibleOrderAndGlobalPageOffset()
    {
        using var grid = new DataGridView
        {
            AllowUserToAddRows = false
        };
        var rowNumberColumn = MainForm.CreateRowNumberColumn();
        grid.Columns.Add(rowNumberColumn);
        grid.Columns.Add("File", "文件");
        grid.Rows.Add(0L, "C.txt");
        grid.Rows.Add(0L, "A.txt");
        grid.Rows.Add(0L, "B.txt");

        grid.Sort(grid.Columns["File"]!, System.ComponentModel.ListSortDirection.Ascending);
        MainForm.UpdateAssetRowNumbers(grid, 101);

        Assert.Equal("行号", rowNumberColumn.HeaderText);
        Assert.Equal(DataGridViewColumnSortMode.NotSortable, rowNumberColumn.SortMode);
        Assert.True(rowNumberColumn.Frozen);
        Assert.Equal(
            [101L, 102L, 103L],
            grid.Rows.Cast<DataGridViewRow>()
                .Select(row => Assert.IsType<long>(row.Cells["RowNumber"].Value)));
        Assert.Equal(
            ["A.txt", "B.txt", "C.txt"],
            grid.Rows.Cast<DataGridViewRow>()
                .Select(row => row.Cells["File"].Value));
    }

    [Fact]
    public void AssetDirectoryLayout_KeepsTheToolbarAndDirectoryGridVisible()
    {
        using var grid = new DataGridView();
        using var openButton = new Button();
        using var summaryLabel = new Label();
        using var layout = MainForm.CreateAssetDirectoryLayout(
            grid,
            openButton,
            summaryLabel);
        layout.Size = new Size(900, 500);
        layout.CreateControl();
        layout.PerformLayout();

        Assert.Contains(grid, Descendants(layout));
        Assert.Contains(openButton, Descendants(layout));
        Assert.Contains(summaryLabel, Descendants(layout));
        Assert.Equal(DockStyle.Fill, grid.Dock);
    }

    [Fact]
    public void AssetDirectoryContextMenu_OffersOpenAndRemoveCommands()
    {
        using var contextMenu = new ContextMenuStrip();
        using var openItem = new ToolStripMenuItem();
        using var removeItem = new ToolStripMenuItem();

        MainForm.ConfigureAssetDirectoryContextMenu(
            contextMenu,
            openItem,
            removeItem);

        Assert.Equal(3, contextMenu.Items.Count);
        Assert.Same(openItem, contextMenu.Items[0]);
        Assert.IsType<ToolStripSeparator>(contextMenu.Items[1]);
        Assert.Same(removeItem, contextMenu.Items[2]);
        Assert.Equal("打开目录位置", openItem.Text);
        Assert.Equal("移除", removeItem.Text);
    }

    [Fact]
    public void AssetDirectoryRemovalConfirmation_ExplainsScopeAndPreservesFiles()
    {
        var path = Path.Combine(Path.GetTempPath(), "Creator Assets");

        var message = MainForm.CreateAssetDirectoryRemovalConfirmation(path);

        Assert.Contains("移除后不再扫描，不计入资源清单", message);
        Assert.Contains(path, message);
        Assert.Contains("不会删除、移动或修改", message);
    }

    [Fact]
    public void OpenDirectoryStartInfo_UsesTheShellWithAnAbsolutePath()
    {
        var directoryPath = Path.Combine(Path.GetTempPath(), "Creator Assets");

        var startInfo = MainForm.CreateOpenDirectoryStartInfo(directoryPath);

        Assert.Equal(Path.GetFullPath(directoryPath), startInfo.FileName);
        Assert.True(startInfo.UseShellExecute);
    }

    private static ScanRoot CreateIdleScanRoot(
        DateTimeOffset lastScannedAt,
        DateTimeOffset updatedAt)
    {
        return new ScanRoot(
            Guid.NewGuid(),
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")),
            ScanRootMode.Readonly,
            true,
            ScanRootStatus.Active,
            updatedAt.AddDays(-1),
            updatedAt,
            lastScannedAt,
            null,
            IdleSchedule: new IdleScanSchedule(
                true,
                10,
                IdleScanIntervalUnit.Minutes));
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }

    private static ManagedObjectStorageBackup CreateCloudBackup(
        Guid assetId,
        long size,
        string objectKey,
        IReadOnlyList<string> projectNames,
        ObjectStorageProvider? provider = null)
    {
        var now = DateTimeOffset.UtcNow;
        var location = new ObjectStorageLocation(
            Guid.NewGuid(),
            assetId,
            Guid.NewGuid(),
            objectKey,
            StorageVerificationStatus.Healthy,
            size,
            new string('a', 64),
            "etag",
            now,
            now,
            now);
        var source = new ObjectStorageRestoreSource(
            assetId,
            "asset.bin",
            size,
            now,
            new string('a', 64),
            location)
        {
            ProjectNames = projectNames
        };
        var profile = provider is null
            ? null
            : new ObjectStorageProfile(
                location.StorageProfileId,
                provider.Value.ToString(),
                provider.Value,
                "storage.example.com",
                "creator-assets",
                "region",
                true,
                "access-key",
                now,
                now);
        return new ManagedObjectStorageBackup(
            source,
            profile,
            profile is not null);
    }
}
