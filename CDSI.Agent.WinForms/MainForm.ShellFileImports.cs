using CDSI.Agent.Application.Assets;
using CDSI.Agent.Core.Assets;

namespace CDSI.Agent.WinForms;

public sealed partial class MainForm
{
    private readonly System.Windows.Forms.Timer _shellImportTimer = new();
    private ShellFileImportBroker? _shellImportBroker;
    private SelectedFileImportService? _selectedFileImportService;
    private string[] _shellImportProtectedDirectories = [];
    private bool _shellImportInProgress;

    internal void BindShellFileImports(ShellFileImportBroker broker,
        SelectedFileImportService service, string profileDirectory)
    {
        _shellImportBroker = broker;
        _selectedFileImportService = service;
        _shellImportProtectedDirectories = [Path.GetDirectoryName(_dataDirectory)!, profileDirectory];
        _shellImportTimer.Interval = 500;
        _shellImportTimer.Tick += async (_, _) => await ProcessPendingShellImportsAsync();
        _shellImportTimer.Start();
        FormClosed += (_, _) => _shellImportTimer.Dispose();
    }

    private async Task ProcessPendingShellImportsAsync()
    {
        if (_shellImportBroker is null || _selectedFileImportService is null ||
            _shellImportBroker.PendingFileCount == 0 || _shellImportInProgress ||
            _databaseBackupShutdownInProgress || RestartForPendingStateRestore || RestartForWorkspaceChange ||
            !Visible || HasOpenIdleScanBlockingModalWindow() || _refreshingAssetPage ||
            !TryBeginStatefulOperation())
        {
            return;
        }

        _shellImportInProgress = true;
        _scanCancellation?.Dispose();
        _scanCancellation = new CancellationTokenSource();
        try
        {
            var paths = _shellImportBroker.Drain();
            if (!_initializationSucceeded || !_readerInitialized)
            {
                MessageBox.Show(this, "Beacon 数据库尚未正常初始化，文件没有登记。请完成数据恢复后重新加入。",
                    "无法登记文件", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            MainWindowActivator.Activate(this);
            _statusLabel.Text = "正在登记选中的文件";
            _progressLabel.Text = $"待登记 {paths.Length:N0} 个文件";
            _progressBar.Style = ProgressBarStyle.Marquee;
            var progress = new Progress<string>(path => _currentPathLabel.Text = path);
            SelectedFileImportResult? result = null;
            var ran = await _stateDatabaseWriteGate.TryRunAsync(async () =>
                result = await Task.Run(() => _selectedFileImportService.ImportAsync(
                    paths, _shellImportProtectedDirectories, progress, _scanCancellation.Token),
                    _scanCancellation.Token));
            if (!ran || result is null)
            {
                throw new InvalidOperationException("数据库写入暂不可用，请重新加入文件。");
            }

            await ResetAssetFilterAsync();
            await RefreshAssetsAsync();
            _mainTabControl.SelectedTab = _assetsTabPage;
            var imported = result.Items.Where(item => item.AssetId is not null)
                .Select(item => item.AssetId!.Value).ToHashSet();
            _assetGrid.ClearSelection();
            foreach (DataGridViewRow row in _assetGrid.Rows)
            {
                if (row.Tag is AssetListItem asset && imported.Contains(asset.AssetId))
                {
                    row.Selected = true;
                }
            }
            _statusLabel.Text = $"文件登记{(result.Cancelled ? "已取消" : "完成")}：" +
                $"已登记 {result.ImportedCount:N0}，未登记 {result.FailedCount:N0}";
            _runtimeLog.WriteInformation(_statusLabel.Text);
            if (result.FailedCount > 0)
            {
                MessageBox.Show(this, string.Join(Environment.NewLine + Environment.NewLine,
                        result.Items.Where(item => item.Error is not null).Take(8)
                            .Select(item => $"{item.Path}\n{item.Error}")),
                    "部分文件未能登记", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        catch (OperationCanceledException)
        {
            _statusLabel.Text = "文件登记已取消";
        }
        catch (Exception exception)
        {
            ShowError("文件登记未能完成", exception);
        }
        finally
        {
            _progressBar.Style = ProgressBarStyle.Blocks;
            _shellImportInProgress = false;
            SetBusy(false);
        }
    }

    private bool CanCloseShellImports(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing && _shellImportInProgress)
        {
            _scanCancellation?.Cancel();
            e.Cancel = true;
            return false;
        }
        if (e.CloseReason == CloseReason.UserClosing && _shellImportBroker?.PendingFileCount > 0 &&
            MessageBox.Show(this, "还有等待登记的文件，退出或重启会取消这些请求。是否继续？",
                "文件等待登记", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            e.Cancel = true;
            return false;
        }
        _shellImportTimer.Stop();
        _shellImportBroker?.StopAccepting();
        return true;
    }
}
