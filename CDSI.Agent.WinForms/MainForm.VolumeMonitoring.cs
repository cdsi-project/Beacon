using CDSI.Agent.Application.Scanning;
using CDSI.Agent.Core.Scanning;

namespace CDSI.Agent.WinForms;

public sealed partial class MainForm
{
    private const int WmDeviceChange = 0x0219;
    private const int DbtDeviceNodesChanged = 0x0007;
    private const int DbtDeviceArrival = 0x8000;
    private const int DbtDeviceRemoveComplete = 0x8004;

    private readonly LocalVolumeReconciliationService _volumeReconciliationService;
    private readonly SemaphoreSlim _volumeReconciliationGate = new(1, 1);
    private CancellationTokenSource? _volumeReconciliationCancellation;
    private bool _localVolumeMonitoringEnabled;
    private bool _localVolumeReconciliationPaused;

    protected override void WndProc(ref Message message)
    {
        base.WndProc(ref message);
        if (_localVolumeMonitoringEnabled &&
            IsLocalVolumeDeviceChange(message.Msg, message.WParam))
        {
            ScheduleLocalVolumeReconciliation();
        }
    }

    internal static bool IsLocalVolumeDeviceChange(int message, nint eventType)
    {
        if (message != WmDeviceChange)
        {
            return false;
        }

        return eventType.ToInt64() is
            DbtDeviceNodesChanged or
            DbtDeviceArrival or
            DbtDeviceRemoveComplete;
    }

    private void EnableLocalVolumeMonitoring()
    {
        _localVolumeMonitoringEnabled = true;
    }

    private void StopLocalVolumeMonitoring()
    {
        _localVolumeMonitoringEnabled = false;
        _volumeReconciliationCancellation?.Cancel();
        _volumeReconciliationCancellation?.Dispose();
        _volumeReconciliationCancellation = null;
        ResumeLocalVolumeMonitoring(enableMonitoring: false);
    }

    private async Task<bool> PauseLocalVolumeMonitoringAsync()
    {
        var wasEnabled = _localVolumeMonitoringEnabled;
        _localVolumeMonitoringEnabled = false;
        var cancellation = _volumeReconciliationCancellation;
        cancellation?.Cancel();

        if (!_localVolumeReconciliationPaused)
        {
            await _volumeReconciliationGate.WaitAsync();
            _localVolumeReconciliationPaused = true;
        }

        if (ReferenceEquals(_volumeReconciliationCancellation, cancellation))
        {
            cancellation?.Dispose();
            _volumeReconciliationCancellation = null;
        }

        return wasEnabled;
    }

    private void ResumeLocalVolumeMonitoring(bool enableMonitoring)
    {
        if (_localVolumeReconciliationPaused)
        {
            _localVolumeReconciliationPaused = false;
            _volumeReconciliationGate.Release();
        }

        _localVolumeMonitoringEnabled = enableMonitoring;
    }

    private async Task<LocalVolumeReconciliationResult> ReconcileLocalVolumesAsync(
        CancellationToken cancellationToken = default)
    {
        await _volumeReconciliationGate.WaitAsync(cancellationToken);
        try
        {
            return await _volumeReconciliationService.ReconcileAsync(
                cancellationToken);
        }
        finally
        {
            _volumeReconciliationGate.Release();
        }
    }

    private void ScheduleLocalVolumeReconciliation()
    {
        _volumeReconciliationCancellation?.Cancel();
        _volumeReconciliationCancellation?.Dispose();
        _volumeReconciliationCancellation = new CancellationTokenSource();
        _ = ReconcileLocalVolumesAfterDelayAsync(
            _volumeReconciliationCancellation.Token);
    }

    private async Task ReconcileLocalVolumesAfterDelayAsync(
        CancellationToken cancellationToken)
    {
        var gateEntered = false;
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(750), cancellationToken);
            while (_isBusy)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }

            await _volumeReconciliationGate.WaitAsync(cancellationToken);
            gateEntered = true;
            if (!_localVolumeMonitoringEnabled)
            {
                return;
            }

            var result = await _volumeReconciliationService.ReconcileAsync(
                cancellationToken);
            if (!result.HasChanges || IsDisposed)
            {
                return;
            }

            await RefreshAssetsAsync();
            _statusLabel.Text = FormatVolumeReconciliationStatus(result);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            if (!IsDisposed)
            {
                _statusLabel.Text = $"移动设备状态更新失败：{exception.Message}";
            }
        }
        finally
        {
            if (gateEntered)
            {
                _volumeReconciliationGate.Release();
            }
        }
    }

    private static string FormatVolumeReconciliationStatus(
        LocalVolumeReconciliationResult result)
    {
        if (result.OfflineVolumes > 0)
        {
            return $"移动设备已离线 {result.OfflineVolumes:N0} 个，资产记录已保留";
        }

        var remapped = result.RemappedScanRoots + result.RemappedAssetLocations;
        if (remapped > 0)
        {
            return $"移动设备盘符已更新，重映射 {remapped:N0} 个本地位置";
        }

        return result.ReconnectedVolumes > 0
            ? $"移动设备已重新连接 {result.ReconnectedVolumes:N0} 个，文件位置等待确认"
            : "本地卷身份已更新";
    }
}
