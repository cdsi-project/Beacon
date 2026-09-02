namespace CDSI.Agent.WinForms;

public sealed partial class MainForm
{
    private bool _databaseBackupInProgress;
    private bool _databaseBackupShutdownInProgress;
    private bool _databaseBackupFinalized;

    private async void DatabaseBackupTimer_Tick(object? sender, EventArgs e)
    {
        await TryCreateAutomaticDatabaseBackupAsync();
    }

    private async Task TryCreateAutomaticDatabaseBackupAsync(
        string? workspacePath = null)
    {
        if (_databaseBackupInProgress)
        {
            return;
        }

        try
        {
            _databaseBackupInProgress = true;
            workspacePath ??= (await _workspaceService.GetAsync())?.Path;
            if (string.IsNullOrWhiteSpace(workspacePath))
            {
                return;
            }

            var result = await _localDatabaseBackupService.CreateSnapshotAsync(
                workspacePath);
            var readerResult = await _readerDatabaseBackupService.CreateSnapshotAsync(
                workspacePath);
            if (result.Created)
            {
                _runtimeLog.WriteInformation(
                    $"已创建本地数据库快照：{result.SnapshotPath}");
            }

            if (readerResult.Created)
            {
                _runtimeLog.WriteInformation(
                    $"已创建 Reader 数据库快照：{readerResult.SnapshotPath}");
            }
        }
        catch (Exception exception)
        {
            _runtimeLog.WriteError("自动创建本地数据库快照失败", exception);
        }
        finally
        {
            _databaseBackupInProgress = false;
            UpdateMainMenuState();
        }
    }

    private async Task CreateDatabaseBackupManuallyAsync()
    {
        if (_databaseBackupInProgress)
        {
            return;
        }

        _databaseBackupInProgress = true;
        UpdateMainMenuState();
        var previousStatus = _statusLabel.Text;
        _statusLabel.Text = "正在备份本地数据库";
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

            var result = await _localDatabaseBackupService.CreateSnapshotAsync(
                workspace.Path,
                force: true);
            var readerResult = await _readerDatabaseBackupService.CreateSnapshotAsync(
                workspace.Path,
                force: true);
            _runtimeLog.WriteInformation(
                $"已手动创建本地数据库快照：{result.SnapshotPath}");
            _runtimeLog.WriteInformation(
                $"已手动创建 Reader 数据库快照：{readerResult.SnapshotPath}");
            _statusLabel.Text = "资产与 Reader 数据库已备份";
            MessageBox.Show(
                this,
                $"数据库备份已完成。\n\n资产数据库：{result.SnapshotPath}\n\nReader 数据库：{readerResult.SnapshotPath}",
                "CDSI Beacon",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception exception)
        {
            _statusLabel.Text = "数据库备份失败";
            ShowError("无法备份本地数据库", exception);
        }
        finally
        {
            if (string.Equals(
                    _statusLabel.Text,
                    "正在备份本地数据库",
                    StringComparison.Ordinal))
            {
                _statusLabel.Text = previousStatus;
            }

            _databaseBackupInProgress = false;
            UpdateMainMenuState();
        }
    }

    private async Task OpenDatabaseBackupDirectoryAsync()
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

            OpenDirectoryPath(
                _localDatabaseBackupService.GetBackupDirectory(workspace.Path),
                "无法打开数据库备份目录");
        }
        catch (Exception exception)
        {
            ShowError("无法读取数据库备份目录", exception);
        }
    }

    private async void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        _scanCancellation?.Cancel();
        StopLocalVolumeMonitoring();
        _databaseBackupTimer.Stop();
        _idleScanTimer.Stop();

        if (_databaseBackupFinalized || e.CloseReason != CloseReason.UserClosing)
        {
            return;
        }

        e.Cancel = true;
        if (_databaseBackupShutdownInProgress)
        {
            return;
        }

        _databaseBackupShutdownInProgress = true;
        try
        {
            var workspace = await _workspaceService.GetAsync();
            if (workspace is not null)
            {
                var result = await _localDatabaseBackupService.CreateSnapshotAsync(
                    workspace.Path);
                var readerResult = await _readerDatabaseBackupService.CreateSnapshotAsync(
                    workspace.Path);
                if (result.Created)
                {
                    _runtimeLog.WriteInformation(
                        $"退出前已创建本地数据库快照：{result.SnapshotPath}");
                }

                if (readerResult.Created)
                {
                    _runtimeLog.WriteInformation(
                        $"退出前已创建 Reader 数据库快照：{readerResult.SnapshotPath}");
                }
            }
        }
        catch (Exception exception)
        {
            _runtimeLog.WriteError("退出前创建本地数据库快照失败", exception);
        }
        finally
        {
            _databaseBackupFinalized = true;
            _databaseBackupShutdownInProgress = false;
            BeginInvoke(Close);
        }
    }
}
