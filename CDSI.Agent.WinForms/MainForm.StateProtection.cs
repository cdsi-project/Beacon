using CDSI.Agent.Infrastructure.Persistence;

namespace CDSI.Agent.WinForms;

public sealed partial class MainForm
{
    private bool _initializationSucceeded;
    private bool _restartForPendingStateRestore;
    private StateRestoreApplyResult? _startupStateRestoreResult;
    private string? _startupStateRestoreWarning;
    private string? _startupStateRestoreSafetyBackupPath;

    internal bool RestartForPendingStateRestore => _restartForPendingStateRestore;

    internal void SetStartupStateRestoreNotification(
        StateRestoreApplyResult? result,
        string? warning,
        string? safetyBackupPath = null)
    {
        _startupStateRestoreResult = result;
        _startupStateRestoreWarning = string.IsNullOrWhiteSpace(warning)
            ? null
            : warning.Trim();
        _startupStateRestoreSafetyBackupPath =
            StartupFailureReporter.NormalizeReportablePath(safetyBackupPath);
    }

    internal static bool CanEnterStateProtection(
        bool isBusy,
        bool databaseBackupInProgress) =>
        !isBusy && !databaseBackupInProgress;

    internal static bool RequiresEmergencyStateRestore(
        bool initializationSucceeded,
        bool readerInitializationSucceeded) =>
        !initializationSucceeded || !readerInitializationSucceeded;

    private async Task OpenStateProtectionAsync()
    {
        if (RequiresEmergencyStateRestore(
                _initializationSucceeded,
                _readerInitialized))
        {
            await OpenEmergencyStateRestoreAsync();
            return;
        }

        if (!CanEnterStateProtection(_isBusy, _databaseBackupInProgress))
        {
            return;
        }

        var stateProtectionEntered = false;
        var resumeVolumeMonitoring = false;
        var volumeMonitoringPauseAttempted = false;
        IDisposable? stateDatabaseWriteSuspension = null;
        try
        {
            _databaseBackupInProgress = true;
            stateProtectionEntered = true;
            SetBusy(true, allowCancel: false);
            _statusLabel.Text = "正在进入数据保护";

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

            stateDatabaseWriteSuspension =
                await _stateDatabaseWriteGate.SuspendAsync();
            volumeMonitoringPauseAttempted = true;
            resumeVolumeMonitoring = await PauseLocalVolumeMonitoringAsync();
            using var dialog = new StateProtectionForm(
                _localStateProtectionService,
                workspace.Path,
                _clientId,
                _runtimeLog);
            dialog.ShowDialog(this);
            if (!dialog.RestartRequested)
            {
                return;
            }

            _runtimeLog.WriteInformation("正在关闭 Beacon，以便应用待处理的状态恢复");
            _restartForPendingStateRestore = true;
            _databaseBackupFinalized = true;
            Close();
        }
        catch (Exception exception)
        {
            ShowError("无法打开数据保护", exception);
        }
        finally
        {
            stateDatabaseWriteSuspension?.Dispose();
            if (stateProtectionEntered && !_restartForPendingStateRestore)
            {
                if (volumeMonitoringPauseAttempted)
                {
                    ResumeLocalVolumeMonitoring(
                        resumeVolumeMonitoring && _initializationSucceeded);
                }

                _databaseBackupInProgress = false;
                SetBusy(false);
            }
        }
    }

    private async Task OfferEmergencyStateRestoreAsync()
    {
        if (MessageBox.Show(
                this,
                CreateEmergencyStateRestoreIntroduction(),
                "从状态备份紧急恢复",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) == DialogResult.Yes)
        {
            await OpenEmergencyStateRestoreAsync();
        }
    }

    private async Task<bool> ResolveMissingStateDatabasesAsync(
        MissingStateDatabases missingDatabases)
    {
        while (!IsDisposed)
        {
            var choice = MessageBox.Show(
                this,
                CreateMissingStateDatabaseRecoveryPrompt(missingDatabases),
                "检测到 Beacon 状态数据库缺失",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button3);
            if (choice == DialogResult.No)
            {
                _runtimeLog.WriteInformation(
                    $"用户确认在既有 Beacon 安装缺失 {missingDatabases} 数据库时创建空白数据库");
                return true;
            }

            if (choice != DialogResult.Yes)
            {
                _databaseBackupFinalized = true;
                Close();
                return false;
            }

            await OpenEmergencyStateRestoreAsync();
            if (_restartForPendingStateRestore || IsDisposed)
            {
                return false;
            }
        }

        return false;
    }

    private async Task OpenEmergencyStateRestoreAsync()
    {
        if (!CanEnterStateProtection(_isBusy, _databaseBackupInProgress))
        {
            MessageBox.Show(
                this,
                "Beacon 正在执行任务或数据库备份，请等待完成后再重试。",
                "从状态备份紧急恢复",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        _databaseBackupInProgress = true;
        SetBusy(true, allowCancel: false);
        var resumeVolumeMonitoring = false;
        var volumeMonitoringPauseAttempted = false;
        IDisposable? stateDatabaseWriteSuspension = null;
        var previousStatus = _statusLabel.Text;
        try
        {
            stateDatabaseWriteSuspension =
                await _stateDatabaseWriteGate.SuspendAsync();
            volumeMonitoringPauseAttempted = true;
            resumeVolumeMonitoring = await PauseLocalVolumeMonitoringAsync();
            using var dialog = new OpenFileDialog
            {
                Title = "选择用于紧急恢复的 Beacon 状态备份",
                Filter = StateProtectionForm.StateBackupFileFilter,
                CheckFileExists = true,
                Multiselect = false,
                RestoreDirectory = true
            };
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            _statusLabel.Text = "正在验证紧急恢复状态备份";
            var backup = await _localStateProtectionService.InspectAsync(dialog.FileName);
            if (backup.Status != LocalStateBackupStatus.Restorable)
            {
                _statusLabel.Text = "状态备份不可用于紧急恢复";
                var message = backup.Status == LocalStateBackupStatus.NewerVersion
                    ? "此备份由更高版本的 Beacon 创建。请先升级 Beacon，再执行紧急恢复。"
                    : backup.Error ?? "状态备份校验失败，文件可能已损坏或被修改。";
                MessageBox.Show(
                    this,
                    message,
                    "无法紧急恢复",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            if (MessageBox.Show(
                    this,
                    CreateEmergencyStateRestoreConfirmation(backup),
                    "确认紧急恢复 Beacon 状态",
                    MessageBoxButtons.OKCancel,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2) != DialogResult.OK)
            {
                _statusLabel.Text = previousStatus;
                return;
            }

            _statusLabel.Text = "正在安排紧急恢复";
            var preparation = await _localStateProtectionService
                .PrepareEmergencyRestoreAsync(backup.Path, _clientId, backup);
            _runtimeLog.WriteInformation(
                $"已安排 Beacon 紧急状态恢复；RestoreId={preparation.RestoreId:D}；" +
                $"状态备份={backup.Path}；恢复前安全副本={preparation.SafetyBackupPath}");
            _restartForPendingStateRestore = true;
            _databaseBackupFinalized = true;
            Close();
        }
        catch (Exception exception)
        {
            _statusLabel.Text = "紧急恢复未能安排";
            ShowError("无法安排紧急状态恢复", exception);
        }
        finally
        {
            stateDatabaseWriteSuspension?.Dispose();
            if (!_restartForPendingStateRestore)
            {
                SetBusy(false);
                if (volumeMonitoringPauseAttempted)
                {
                    ResumeLocalVolumeMonitoring(
                        resumeVolumeMonitoring && _initializationSucceeded);
                }

                _databaseBackupInProgress = false;
                UpdateMainMenuState();
            }
        }
    }

    private void ShowStartupStateRestoreNotification()
    {
        if (_startupStateRestoreResult is { } result)
        {
            _startupStateRestoreResult = null;
            if (_initializationSucceeded)
            {
                _statusLabel.Text = "Beacon 状态恢复完成";
            }

            MessageBox.Show(
                this,
                CreateStartupStateRestoreSuccessMessage(
                    result,
                    _initializationSucceeded),
                _initializationSucceeded
                    ? "状态恢复完成"
                    : "状态已替换，但初始化失败",
                MessageBoxButtons.OK,
                _initializationSucceeded
                    ? MessageBoxIcon.Information
                    : MessageBoxIcon.Warning);
            return;
        }

        if (_startupStateRestoreWarning is not { } warning)
        {
            return;
        }

        _startupStateRestoreWarning = null;
        if (_initializationSucceeded)
        {
            _statusLabel.Text = "状态恢复未完成";
        }

        MessageBox.Show(
            this,
            CreateStartupStateRestoreWarningMessage(
                warning,
                _startupStateRestoreSafetyBackupPath,
                _initializationSucceeded),
            _initializationSucceeded
                ? "状态恢复未完成"
                : "状态恢复和初始化均未完成",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
        _startupStateRestoreSafetyBackupPath = null;
    }

    internal static string CreateEmergencyStateRestoreIntroduction() =>
        "Beacon 状态数据库初始化失败，当前数据库可能丢失或损坏。\n\n" +
        "可以绕过当前数据库，选择一个已导出的 .cdsibak 状态备份执行紧急恢复。" +
        "恢复将在 Beacon 重新启动时完成；现有数据库原始文件会先复制到独立隔离目录。\n\n" +
        "是否现在选择状态备份？";

    internal static string CreateMissingStateDatabaseRecoveryPrompt(
        MissingStateDatabases missingDatabases)
    {
        var explanation = missingDatabases switch
        {
            MissingStateDatabases.Asset =>
                "检测到此 Windows 用户以前运行过 Beacon，但资产数据库 cdsi.db 已不存在。" +
                "直接继续会创建新的空白资产库，原有资产索引、项目、标签和发布记录不会自动恢复。",
            MissingStateDatabases.Reader =>
                "检测到此 Windows 用户以前运行过 Beacon，但 RSS订阅数据库 reader.db 已不存在。" +
                "原有 RSS订阅、阅读进度和收藏状态可能无法恢复。\n\n" +
                "如果这是从未使用 RSS，或从旧版本首次升级，可以选择继续，Beacon 将创建空白 RSS 库。",
            MissingStateDatabases.Asset | MissingStateDatabases.Reader =>
                "检测到此 Windows 用户以前运行过 Beacon，但资产数据库 cdsi.db 和 RSS订阅数据库 reader.db 均已不存在。" +
                "直接继续会创建空白资产库和空白 RSS 库；原有资产索引、项目、标签、发布记录、RSS订阅、阅读进度和收藏状态不会自动恢复。\n\n" +
                "如果 reader.db 缺失只是因为从未使用 RSS，或从旧版本首次升级，空白 RSS 库可以正常创建；资产库仍将是空白状态。",
            _ => throw new ArgumentOutOfRangeException(
                nameof(missingDatabases),
                missingDatabases,
                "At least one known state database must be missing.")
        };

        return
            $"{explanation}\n\n" +
            "是：选择 .cdsibak 状态备份并执行紧急恢复\n" +
            "否：我确认继续并创建缺失的空白数据库\n" +
            "取消：退出 Beacon，不创建数据库";
    }

    internal static string CreateEmergencyStateRestoreConfirmation(
        LocalStateBackupInfo backup)
    {
        ArgumentNullException.ThrowIfNull(backup);
        var createdAt = backup.CreatedAtUtc is { } value
            ? value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
            : "未知时间";
        return
            $"将使用 {createdAt} 创建的状态备份执行紧急恢复。\n\n" +
            "此流程不读取当前数据库。重新启动后，当前资产数据库、RSS订阅数据库及其 SQLite 辅助文件会先复制到独立隔离目录，然后由所选备份替换。\n\n" +
            "本地素材文件、云端对象和当前客户端 ID 不会被修改。\n\n" +
            $"{StateProtectionForm.CreateCredentialBoundaryNotice()}\n\n" +
            $"{StateProtectionForm.CreateSensitiveMetadataNotice()}\n\n" +
            "确定继续吗？";
    }

    internal static string CreateStartupStateRestoreSuccessMessage(
        StateRestoreApplyResult result,
        bool initializationSucceeded)
    {
        ArgumentNullException.ThrowIfNull(result);
        var outcome = initializationSucceeded
            ? $"Beacon 状态已恢复到 {result.BackupCreatedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}。"
            : "状态数据库已经替换，但 Beacon 主窗口初始化仍然失败。应用尚未恢复到可正常使用状态，请查看运行日志，或从“文件 > 数据保护”选择另一份状态备份紧急恢复。";
        return
            $"{outcome}\n\n" +
            "已恢复资产数据库和 RSS订阅数据库。本地素材文件、云端对象和当前客户端 ID 未更改。\n\n" +
            $"{StateProtectionForm.CreateCredentialBoundaryNotice()}\n\n" +
            "云存储、OpenWeb 或 Git 连接可能需要重新授权。\n\n" +
            $"恢复前安全副本位置：\n{result.SafetyBackupPath}";
    }

    internal static string CreateStartupStateRestoreWarningMessage(
        string warning,
        string? safetyBackupPath,
        bool initializationSucceeded)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(warning);
        var initializationNotice = initializationSucceeded
            ? string.Empty
            : "\n\nBeacon 主窗口初始化也未完成，请从“文件 > 数据保护”选择一份状态备份紧急恢复。";
        var reportableSafetyPath =
            StartupFailureReporter.NormalizeReportablePath(safetyBackupPath);
        var safetyNotice = reportableSafetyPath is null
            ? "\n\n恢复前安全副本位置：未生成或无法确定。"
            : $"\n\n恢复前安全副本位置：\n{reportableSafetyPath}";
        return $"{warning.Trim()}{initializationNotice}{safetyNotice}";
    }
}
