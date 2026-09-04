using CDSI.Agent.Core.Fingerprints;
using CDSI.Agent.Core.Scanning;

namespace CDSI.Agent.WinForms;

public sealed partial class MainForm
{
    private readonly System.Windows.Forms.Timer _idleScanTimer = new();
    private bool _idleScanCheckInProgress;

    private void ConfigureIdleScanScheduler()
    {
        _idleScanTimer.Interval = (int)TimeSpan.FromSeconds(30).TotalMilliseconds;
        _idleScanTimer.Tick += IdleScanTimer_Tick;
    }

    private async void IdleScanTimer_Tick(object? sender, EventArgs e)
    {
        await TryStartDueIdleScanAsync();
    }

    private async Task TryStartDueIdleScanAsync()
    {
        if (!CanStartIdleScan(
                _isBusy,
                _idleScanCheckInProgress,
                HasOpenIdleScanBlockingModalWindow(),
                _databaseBackupInProgress))
        {
            return;
        }

        _idleScanCheckInProgress = true;
        try
        {
            var roots = await _scanService.ListScanRootsAsync();
            if (!CanStartIdleScan(
                    _isBusy,
                    checkInProgress: false,
                    hasBlockingModalWindow: HasOpenIdleScanBlockingModalWindow(),
                    stateProtectionInProgress: _databaseBackupInProgress))
            {
                return;
            }

            var dueRoots = GetDueIdleScanRoots(roots, DateTimeOffset.UtcNow);
            if (dueRoots.Count == 0)
            {
                return;
            }

            _runtimeLog.WriteInformation(
                $"开始空闲扫描；目录数={dueRoots.Count}；" +
                $"目录={string.Join(" | ", dueRoots.Select(root => root.Path))}");
            await RunScanPipelineAsync(
                dueRoots.Select(root => root.Id).ToArray(),
                isInitialScan: false,
                fingerprintMode: FingerprintMode.DuplicateCandidates,
                isIdleScan: true);
        }
        catch (Exception exception)
        {
            _runtimeLog.WriteError("检查空闲扫描计划失败", exception);
        }
        finally
        {
            _idleScanCheckInProgress = false;
        }
    }

    internal static IReadOnlyList<ScanRoot> GetDueIdleScanRoots(
        IEnumerable<ScanRoot> roots,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(roots);
        return roots
            .Where(root =>
                root.Mode == ScanRootMode.Readonly &&
                root.Enabled &&
                root.Status != ScanRootStatus.Offline &&
                root.GetIdleScanSchedule().IsDue(
                    GetIdleScanAnchor(root),
                    now))
            .ToArray();
    }

    internal static bool CanStartIdleScan(
        bool isBusy,
        bool checkInProgress,
        bool hasBlockingModalWindow,
        bool stateProtectionInProgress)
    {
        return !isBusy &&
            !checkInProgress &&
            !hasBlockingModalWindow &&
            !stateProtectionInProgress;
    }

    private static DateTimeOffset GetIdleScanAnchor(ScanRoot root)
    {
        return root.LastScannedAt is { } lastScannedAt &&
            lastScannedAt > root.UpdatedAt
                ? lastScannedAt
                : root.UpdatedAt;
    }

    private bool HasOpenIdleScanBlockingModalWindow()
    {
        return System.Windows.Forms.Application.OpenForms
            .Cast<Form>()
            .Any(form =>
                !ReferenceEquals(form, this) &&
                form.Modal &&
                IsIdleScanBlockingModal(form));
    }

    internal static bool IsIdleScanBlockingModal(Form form)
    {
        ArgumentNullException.ThrowIfNull(form);
        return form is not TaskCenterForm;
    }
}
