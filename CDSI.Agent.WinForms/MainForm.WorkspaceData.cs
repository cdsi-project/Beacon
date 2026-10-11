using CDSI.Agent.Application.Workspaces;
using CDSI.Agent.Infrastructure.FileSystem;
using CDSI.Agent.Infrastructure.Persistence;

namespace CDSI.Agent.WinForms;

public sealed partial class MainForm
{
    private string? _selectedWorkspacePath;
    private WorkspaceDataDirectoryService? _workspaceDataDirectoryService;
    internal bool RestartForWorkspaceChange { get; private set; }

    internal void BindWorkspaceDataLocation(
        string workspacePath, WorkspaceDataDirectoryService service)
    {
        _selectedWorkspacePath = Path.GetFullPath(workspacePath);
        _workspaceDataDirectoryService = service;
    }

    private async Task SwitchWorkspaceDataAsync(string workspacePath)
    {
        if (_workspaceDataDirectoryService is null ||
            !_initializationSucceeded || !_readerInitialized)
        {
            throw new InvalidOperationException("请先完成数据库初始化或恢复，再更改工作目录。");
        }

        if (!TryBeginStatefulOperation(allowCancel: false))
        {
            throw new InvalidOperationException("当前有任务或备份正在进行，请稍后再试。");
        }

        IDisposable? suspension = null;
        var resumeMonitoring = false;
        _databaseBackupInProgress = true;
        try
        {
            suspension = await _stateDatabaseWriteGate.SuspendAsync();
            resumeMonitoring = await PauseLocalVolumeMonitoringAsync();
            _statusLabel.Text = "正在迁移工作目录数据库";
            await _workspaceDataDirectoryService.ImportAsync(
                workspacePath,
                Path.Combine(_dataDirectory, "cdsi.db"),
                Path.Combine(_dataDirectory, "reader.db"),
                GetApplicationVersion(),
                async staging =>
                {
                    var stagedRepository = new SqliteAssetRepository(Path.Combine(staging, "cdsi.db"));
                    await stagedRepository.InitializeAsync();
                    await new WorkspaceApplicationService(stagedRepository, new WorkspaceProvisioner())
                        .ConfigureAsync(workspacePath);
                });
            _workspaceDataDirectoryService.MarkInitialized(workspacePath);
            _runtimeLog.WriteInformation(
                $"工作目录数据库已复制并校验，重启后切换到 {workspacePath}；旧数据与素材保留");
            RestartForWorkspaceChange = true;
            _databaseBackupFinalized = true;
        }
        finally
        {
            suspension?.Dispose();
            if (!RestartForWorkspaceChange)
            {
                _databaseBackupInProgress = false;
                ResumeLocalVolumeMonitoring(resumeMonitoring);
                SetBusy(false);
            }
        }
    }
}
