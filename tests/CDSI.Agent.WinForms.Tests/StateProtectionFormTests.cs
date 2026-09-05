using CDSI.Agent.Infrastructure.Persistence;
using CDSI.Agent.WinForms;

namespace CDSI.Agent.WinForms.Tests;

public sealed class StateProtectionFormTests
{
    [Fact]
    public void Form_UsesAResizableDpiAwareLayoutAndExplainsBackupScope()
    {
        using var directory = new TestDirectory();
        var dataDirectory = Path.Combine(directory.Path, "Data");
        var workspacePath = Path.Combine(directory.Path, "Workspace");
        var service = new LocalStateProtectionService(
            dataDirectory,
            Path.Combine(dataDirectory, "cdsi.db"),
            Path.Combine(dataDirectory, "reader.db"),
            "0.2.17");
        var runtimeLog = new RuntimeLogService(dataDirectory);
        using var form = new StateProtectionForm(
            service,
            workspacePath,
            Guid.NewGuid().ToString("D"),
            runtimeLog);

        var controls = Descendants(form).ToArray();
        var scope = Assert.Single(
            controls.OfType<Label>(),
            label => label.AccessibleName == "状态备份范围");
        var safetyNotice = Assert.Single(
            controls.OfType<Label>(),
            label => label.AccessibleName == "状态备份安全提示");
        var exclusions = Assert.Single(
            controls.OfType<Label>(),
            label => label.AccessibleName == "状态备份排除范围");
        var privacyNotice = Assert.Single(
            controls.OfType<Label>(),
            label => label.AccessibleName == "状态备份敏感信息说明");
        var grid = Assert.Single(
            controls.OfType<DataGridView>(),
            control => control.AccessibleName == "状态备份列表");

        Assert.Equal("数据保护", form.Text);
        Assert.Equal(AutoScaleMode.Dpi, form.AutoScaleMode);
        Assert.Equal(FormBorderStyle.Sizable, form.FormBorderStyle);
        Assert.Equal(new Size(760, 480), form.MinimumSize);
        Assert.Equal(DockStyle.Fill, grid.Dock);
        Assert.Contains("资产", scope.Text);
        Assert.Contains("RSS订阅", scope.Text);
        Assert.Contains("素材文件", exclusions.Text);
        Assert.Contains("Windows 凭据管理器", exclusions.Text);
        Assert.Contains("由 Beacon 管理的密码/令牌", exclusions.Text);
        Assert.Contains("SSH 私钥", exclusions.Text);
        Assert.Contains("client-identity.json", exclusions.Text);
        Assert.Contains("绝对路径", privacyNotice.Text);
        Assert.Contains("来源客户端 ID", privacyNotice.Text);
        Assert.Contains("RSS URL/内容", privacyNotice.Text);
        Assert.Contains("账号与连接元数据", privacyNotice.Text);
        Assert.Contains("私密数据保存", privacyNotice.Text);
        Assert.Contains("不会替换目标客户端身份", privacyNotice.Text);
        Assert.Contains("状态包当前未加密", safetyNotice.Text);
        Assert.Contains("同盘备份不能防止整块磁盘损坏", safetyNotice.Text);
        Assert.False(scope.AutoEllipsis);
        Assert.False(exclusions.AutoEllipsis);
        Assert.False(privacyNotice.AutoEllipsis);
        Assert.False(safetyNotice.AutoEllipsis);
        var header = Assert.IsType<TableLayoutPanel>(privacyNotice.Parent);
        Assert.True(header.RowStyles[header.GetRow(exclusions)].Height >= 40);
        Assert.True(header.RowStyles[header.GetRow(privacyNotice)].Height >= 40);
        Assert.Contains(
            controls.OfType<Button>(),
            button => button.Text == "立即创建状态备份");
        Assert.Contains(
            controls.OfType<Button>(),
            button => button.Text == "验证所选");
        Assert.Contains(
            controls.OfType<Button>(),
            button => button.Text == "恢复所选备份...");
    }

    [Fact]
    public void ConfigureStateBackupGrid_UsesReadonlySortableRawColumns()
    {
        using var grid = new DataGridView();

        StateProtectionForm.ConfigureStateBackupGrid(grid);

        Assert.True(grid.ReadOnly);
        Assert.False(grid.MultiSelect);
        Assert.True(grid.AllowUserToOrderColumns);
        Assert.True(grid.AllowUserToResizeColumns);
        Assert.Equal(DataGridViewSelectionMode.FullRowSelect, grid.SelectionMode);
        Assert.Equal(typeof(DateTime), grid.Columns["CreatedAt"]?.ValueType);
        Assert.Equal(typeof(long), grid.Columns["Size"]?.ValueType);
        Assert.Equal(
            [
                "创建时间",
                "类型",
                "Beacon 版本",
                "内容",
                "大小",
                "验证状态",
                "位置"
            ],
            grid.Columns
                .Cast<DataGridViewColumn>()
                .Select(column => column.HeaderText));
    }

    [Theory]
    [InlineData(LocalStateBackupStatus.Restorable, false, true)]
    [InlineData(LocalStateBackupStatus.Restorable, true, false)]
    [InlineData(LocalStateBackupStatus.Invalid, false, false)]
    [InlineData(LocalStateBackupStatus.NewerVersion, false, false)]
    public void CanRestoreStateBackup_RequiresAValidatedSelection(
        LocalStateBackupStatus status,
        bool busy,
        bool expected)
    {
        Assert.Equal(
            expected,
            StateProtectionForm.CanRestoreStateBackup(
                CreateBackup(status),
                busy));
        Assert.False(StateProtectionForm.CanRestoreStateBackup(null, busy: false));
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    public void ShouldCancelClose_ProtectsOnlyAnUnfinishedBusyOperation(
        bool busy,
        bool restartRequested,
        bool expected)
    {
        Assert.Equal(
            expected,
            StateProtectionForm.ShouldCancelClose(busy, restartRequested));
    }

    [Fact]
    public void RestoreConfirmation_ExplainsReplacementAndExclusions()
    {
        var confirmation = StateProtectionForm.CreateStateRestoreConfirmation(
            CreateBackup(LocalStateBackupStatus.Restorable));

        Assert.Contains("资产索引、项目、标签", confirmation);
        Assert.Contains("RSS订阅、条目、已读和收藏状态", confirmation);
        Assert.Contains("本地素材文件和云端对象", confirmation);
        Assert.Contains("Windows 凭据管理器", confirmation);
        Assert.Contains("密码/令牌", confirmation);
        Assert.Contains("SSH 私钥", confirmation);
        Assert.Contains("绝对路径", confirmation);
        Assert.Contains("RSS URL/内容", confirmation);
        Assert.Contains("当前 Beacon 客户端 ID", confirmation);
        Assert.Contains("恢复前安全备份", confirmation);
        Assert.Contains("关闭", confirmation);
        Assert.Contains("重新启动", confirmation);
    }

    [Fact]
    public void BackupFilenameAndFilter_UseThePortableStateBundleExtension()
    {
        var filename = StateProtectionForm.CreateStateBackupFilename(
            new DateTimeOffset(2026, 9, 4, 8, 9, 10, TimeSpan.Zero));

        Assert.Equal("beacon-state-20260904-080910Z.cdsibak", filename);
        Assert.Contains("*.cdsibak", StateProtectionForm.StateBackupFileFilter);
    }

    [Fact]
    public void StateProtectionMenuItem_IsPlacedInTheToolsMenu()
    {
        using var toolsMenu = new ToolStripMenuItem("工具");
        using var taskCenterItem = new ToolStripMenuItem("任务中心");
        using var runtimeLogItem = new ToolStripMenuItem("运行日志");
        using var item = new ToolStripMenuItem();

        MainForm.ConfigureStateProtectionMenuItem(item);
        MainForm.ConfigureToolsMenu(
            toolsMenu,
            taskCenterItem,
            runtimeLogItem,
            item);

        Assert.Equal("数据保护(&P)...", item.Text);
        Assert.Equal("数据保护", item.AccessibleName);
        Assert.Equal(Keys.None, item.ShortcutKeys);
        Assert.Collection(
            toolsMenu.DropDownItems.Cast<ToolStripItem>(),
            menuItem => Assert.Same(taskCenterItem, menuItem),
            menuItem => Assert.Same(runtimeLogItem, menuItem),
            menuItem => Assert.IsType<ToolStripSeparator>(menuItem),
            menuItem => Assert.Same(item, menuItem));
    }

    [Fact]
    public void RestartHelperArguments_AreStrictAndDoNotUseTheShell()
    {
        var parentProcessId = Environment.ProcessId == 54321 ? 54322 : 54321;

        Assert.True(Program.TryParsePendingRestoreRestartHelper(
            ["--restart-for-pending-state-restore", parentProcessId.ToString()],
            out var parsed));
        Assert.Equal(parentProcessId, parsed);
        Assert.False(Program.TryParsePendingRestoreRestartHelper([], out _));
        Assert.False(Program.TryParsePendingRestoreRestartHelper(
            ["--restart-for-pending-state-restore", "not-a-process"],
            out _));

        var executablePath = Path.Combine(
            Path.GetTempPath(),
            "CDSI Beacon",
            "CDSI-Beacon.exe");
        var startInfo = Program.CreatePendingRestoreRestartHelperStartInfo(
            executablePath,
            parentProcessId);

        Assert.Equal(Path.GetFullPath(executablePath), startInfo.FileName);
        Assert.False(startInfo.UseShellExecute);
        Assert.True(startInfo.CreateNoWindow);
        Assert.Equal(
            ["--restart-for-pending-state-restore", parentProcessId.ToString()],
            startInfo.ArgumentList);
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    public void CanEnterStateProtection_RequiresBothActivityGatesToBeIdle(
        bool isBusy,
        bool databaseBackupInProgress,
        bool expected)
    {
        Assert.Equal(
            expected,
            MainForm.CanEnterStateProtection(isBusy, databaseBackupInProgress));
    }

    [Theory]
    [InlineData(false, false, false, true)]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, true, false)]
    public void CanBeginStatefulOperation_RequiresAllActivityGatesToBeIdle(
        bool isBusy,
        bool databaseBackupInProgress,
        bool stateDatabaseWritesSuspended,
        bool expected)
    {
        Assert.Equal(
            expected,
            MainForm.CanBeginStatefulOperation(
                isBusy,
                databaseBackupInProgress,
                stateDatabaseWritesSuspended));
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public void RequiresEmergencyStateRestore_RequiresBothDatabasesToInitialize(
        bool initializationSucceeded,
        bool readerInitializationSucceeded,
        bool expected)
    {
        Assert.Equal(
            expected,
            MainForm.RequiresEmergencyStateRestore(
                initializationSucceeded,
                readerInitializationSucceeded));
    }

    [Theory]
    [InlineData(true, false, true, (int)MissingStateDatabases.Asset)]
    [InlineData(true, true, false, (int)MissingStateDatabases.Reader)]
    [InlineData(true, false, false,
        (int)(MissingStateDatabases.Asset | MissingStateDatabases.Reader))]
    [InlineData(false, true, false, (int)MissingStateDatabases.Reader)]
    [InlineData(false, false, true, (int)MissingStateDatabases.Asset)]
    [InlineData(false, false, false, (int)MissingStateDatabases.None)]
    [InlineData(true, true, true, (int)MissingStateDatabases.None)]
    public void MissingStateDatabases_UseAnySurvivingInstallationEvidence(
        bool clientIdentityExistedBeforeStartup,
        bool assetDatabaseExistedBeforeStartup,
        bool readerDatabaseExistedBeforeStartup,
        int expected)
    {
        Assert.Equal(
            (MissingStateDatabases)expected,
            Program.GetMissingStateDatabases(
                clientIdentityExistedBeforeStartup,
                assetDatabaseExistedBeforeStartup,
                readerDatabaseExistedBeforeStartup));
    }

    [Fact]
    public void MissingAssetDatabasePrompt_RequiresAnExplicitStartupChoice()
    {
        var message = MainForm.CreateMissingStateDatabaseRecoveryPrompt(
            MissingStateDatabases.Asset);

        Assert.Contains("以前运行过 Beacon", message);
        Assert.Contains("cdsi.db 已不存在", message);
        Assert.Contains("选择 .cdsibak", message);
        Assert.Contains("确认继续并创建缺失的空白数据库", message);
        Assert.Contains("退出 Beacon，不创建数据库", message);
        Assert.DoesNotContain("reader.db", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MissingReaderDatabasePrompt_AllowsAnExplicitUpgradeOrUnusedRssChoice()
    {
        var message = MainForm.CreateMissingStateDatabaseRecoveryPrompt(
            MissingStateDatabases.Reader);

        Assert.Contains("reader.db 已不存在", message);
        Assert.Contains("从未使用 RSS", message);
        Assert.Contains("从旧版本首次升级", message);
        Assert.Contains("创建空白 RSS 库", message);
        Assert.Contains("选择 .cdsibak", message);
        Assert.Contains("退出 Beacon，不创建数据库", message);
        Assert.DoesNotContain("cdsi.db", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MissingBothDatabasesPrompt_ExplainsBothDataSetsAndExplicitChoices()
    {
        var message = MainForm.CreateMissingStateDatabaseRecoveryPrompt(
            MissingStateDatabases.Asset | MissingStateDatabases.Reader);

        Assert.Contains("cdsi.db", message);
        Assert.Contains("reader.db", message);
        Assert.Contains("资产库仍将是空白状态", message);
        Assert.Contains("从旧版本首次升级", message);
        Assert.Contains("选择 .cdsibak", message);
        Assert.Contains("确认继续并创建缺失的空白数据库", message);
        Assert.Contains("退出 Beacon，不创建数据库", message);
    }

    [Fact]
    public void EmergencyRestoreMessages_ExplainRepositoryIndependentRecovery()
    {
        var introduction = MainForm.CreateEmergencyStateRestoreIntroduction();
        var confirmation = MainForm.CreateEmergencyStateRestoreConfirmation(
            CreateBackup(LocalStateBackupStatus.Restorable));

        Assert.Contains("初始化失败", introduction);
        Assert.Contains("绕过当前数据库", introduction);
        Assert.Contains("独立隔离目录", introduction);
        Assert.Contains("不读取当前数据库", confirmation);
        Assert.Contains("SQLite 辅助文件", confirmation);
        Assert.Contains("客户端 ID", confirmation);
        Assert.Contains("Windows 凭据管理器", confirmation);
        Assert.Contains("RSS URL/内容", confirmation);
    }

    [Fact]
    public void StartupRestoreSuccessMessage_PreservesInitializationFailureAndSafetyPath()
    {
        var safetyPath = Path.Combine(Path.GetTempPath(), "Beacon Safety", "restore-1");
        var result = new StateRestoreApplyResult(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new DateTimeOffset(2026, 9, 4, 8, 9, 10, TimeSpan.Zero),
            safetyPath);

        var message = MainForm.CreateStartupStateRestoreSuccessMessage(
            result,
            initializationSucceeded: false);

        Assert.Contains("状态数据库已经替换", message);
        Assert.Contains("主窗口初始化仍然失败", message);
        Assert.Contains("Windows 凭据管理器", message);
        Assert.DoesNotContain("未包含在状态备份中", message);
        Assert.Contains(Path.GetFullPath(safetyPath), message);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void StartupRestoreWarningMessage_ReportsSafetyPathAndInitializationState(
        bool initializationSucceeded,
        bool mentionsInitializationFailure)
    {
        var safetyPath = Path.Combine(Path.GetTempPath(), "Beacon Safety", "restore-2");

        var message = MainForm.CreateStartupStateRestoreWarningMessage(
            "恢复失败。",
            safetyPath,
            initializationSucceeded);

        Assert.Contains(Path.GetFullPath(safetyPath), message);
        Assert.Equal(
            mentionsInitializationFailure,
            message.Contains("主窗口初始化也未完成", StringComparison.Ordinal));
    }

    [Fact]
    public void SetBackgroundTimerRunning_TracksInitializationOutcome()
    {
        using var timer = new System.Windows.Forms.Timer();

        MainForm.SetBackgroundTimerRunning(timer, shouldRun: true);
        Assert.True(timer.Enabled);

        MainForm.SetBackgroundTimerRunning(timer, shouldRun: false);
        Assert.False(timer.Enabled);
    }

    private static LocalStateBackupInfo CreateBackup(LocalStateBackupStatus status)
    {
        return new LocalStateBackupInfo(
            Path.Combine(Path.GetTempPath(), "beacon-state.cdsibak"),
            Guid.NewGuid(),
            new DateTimeOffset(2026, 9, 4, 8, 9, 10, TimeSpan.Zero),
            "0.2.17",
            LocalStateBackupKind.Manual,
            1024,
            status,
            status == LocalStateBackupStatus.Restorable ? null : "invalid");
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

    private sealed class TestDirectory : IDisposable
    {
        public TestDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "cdsi-agent-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
