using CDSI.Agent.Core.Assets;
using CDSI.Agent.Core.Storage;

namespace CDSI.Agent.WinForms;

public sealed partial class MainForm
{
    private readonly TransferSpeedTracker _restoreSpeedTracker = new();

    private async Task RestoreSelectedAssetsFromOssAsync()
    {
        var selected = GetSelectedAssets()
            .GroupBy(asset => asset.AssetId)
            .Select(group => group.First())
            .ToArray();
        if (selected.Length == 0)
        {
            return;
        }

        if (!TryBeginStatefulOperation())
        {
            return;
        }

        try
        {
            await RestoreSelectedAssetsFromOssCoreAsync(selected);
        }
        finally
        {
            _progressBar.Style = ProgressBarStyle.Blocks;
            SetBusy(false);
        }
    }

    private async Task RestoreSelectedAssetsFromOssCoreAsync(
        IReadOnlyList<AssetListItem> selected)
    {
        _progressBar.Style = ProgressBarStyle.Marquee;
        _progressBar.MarqueeAnimationSpeed = 24;
        _progressLabel.Text = "正在准备从 OSS 取回资产";
        _currentPathLabel.Text = string.Empty;
        _statusLabel.Text = "正在准备 OSS 取回";

        IReadOnlyList<ObjectStorageRestoreCandidate> candidates;
        try
        {
            candidates = await _objectStorageRestoreService.ListCandidatesAsync(
                selected.Select(asset => asset.AssetId).ToArray());
        }
        catch (Exception exception)
        {
            ShowError("无法读取 OSS 备份", exception);
            return;
        }

        var candidateMap = candidates.ToDictionary(candidate => candidate.AssetId);
        var unavailable = selected
            .Where(asset =>
                !candidateMap.TryGetValue(asset.AssetId, out var candidate) ||
                !candidate.Sources.Any(source =>
                    source.HasStoredSecret &&
                    source.Source.Location.Status ==
                        StorageVerificationStatus.Healthy &&
                    !string.IsNullOrWhiteSpace(source.Source.Location.Sha256)))
            .Select(asset => asset.OriginalFilename)
            .Take(8)
            .ToArray();
        if (unavailable.Length > 0)
        {
            MessageBox.Show(
                this,
                $"以下资产没有带有效凭据且已通过校验的 OSS 备份：{Environment.NewLine}{string.Join(Environment.NewLine, unavailable)}",
                "CDSI Beacon",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        string? workspacePath;
        try
        {
            workspacePath = (await _workspaceService.GetAsync())?.Path;
        }
        catch (Exception exception)
        {
            ShowError("无法读取 CDSI 工作目录", exception);
            return;
        }

        using var confirmation = new OssRestoreConfirmationForm(
            selected.Select(asset => candidateMap[asset.AssetId]).ToArray(),
            workspacePath);
        if (confirmation.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        _scanCancellation?.Dispose();
        _restoreSpeedTracker.Reset();
        _scanCancellation = new CancellationTokenSource();
        var restoreProgress = new Progress<ObjectStorageRestoreProgress>(
            UpdateRestoreProgress);

        _progressBar.MarqueeAnimationSpeed = 0;
        _progressBar.Style = ProgressBarStyle.Continuous;
        _progressBar.Minimum = 0;
        _progressBar.Maximum = 1_000;
        _progressBar.Value = 0;
        _statusLabel.Text = "正在从 OSS 取回资产";

        try
        {
            var result = await _objectStorageRestoreService.RestoreAsync(
                confirmation.SelectedRequests,
                confirmation.Destination,
                restoreProgress,
                _scanCancellation.Token);
            await RefreshAssetsAsync();
            ShowRestoreResult(result);
        }
        catch (OperationCanceledException)
        {
            _statusLabel.Text = "OSS 取回已取消";
        }
        catch (Exception exception)
        {
            _statusLabel.Text = "OSS 取回失败";
            ShowError("OSS 取回未能完成", exception);
        }
    }

    private void UpdateRestoreProgress(ObjectStorageRestoreProgress progress)
    {
        var bytesPerSecond = _restoreSpeedTracker.Update(
            progress.NetworkTransferredBytes);
        var speedText = bytesPerSecond <= 0
            ? "--"
            : $"{FormatFileSize((long)bytesPerSecond)}/s";
        _progressLabel.Text =
            $"文件 {progress.ProcessedItems:N0}/{progress.TotalItems:N0} · {FormatFileSize(progress.RestoredBytes)}/{FormatFileSize(progress.TotalBytes)} · 下载 {speedText}";
        _currentPathLabel.Text = progress.Message is null
            ? progress.CurrentPath ?? string.Empty
            : $"{progress.Message} · {progress.CurrentPath}";
        _progressBar.Value = progress.TotalBytes == 0
            ? 0
            : (int)Math.Clamp(
                progress.RestoredBytes * 1_000d / progress.TotalBytes,
                0d,
                1_000d);
    }

    private void ShowRestoreResult(ObjectStorageRestoreResult result)
    {
        _statusLabel.Text = result.Status switch
        {
            RestoreJobStatus.Completed =>
                $"OSS 取回完成，共 {result.CompletedItems:N0} 个资产",
            RestoreJobStatus.Cancelled =>
                $"OSS 取回已取消，已完成 {result.CompletedItems:N0} 个资产",
            _ =>
                $"OSS 取回完成 {result.CompletedItems:N0} 个，失败 {result.FailedItems:N0} 个"
        };

        if (result.Status == RestoreJobStatus.Completed)
        {
            MessageBox.Show(
                this,
                $"取回和完整性校验完成，共处理 {result.CompletedItems:N0} 个资产。",
                "CDSI Beacon",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        var errorLines = result.Items
            .Where(item => !string.IsNullOrWhiteSpace(item.ErrorMessage))
            .Take(8)
            .Select(item =>
                $"{item.TargetPath}{Environment.NewLine}{item.ErrorMessage}")
            .ToArray();
        var remaining = result.Items.Count(item =>
            !string.IsNullOrWhiteSpace(item.ErrorMessage)) - errorLines.Length;
        var details = string.Join(
            Environment.NewLine + Environment.NewLine,
            errorLines);
        if (remaining > 0)
        {
            details +=
                $"{Environment.NewLine}{Environment.NewLine}另有 {remaining:N0} 个错误，详情已写入本地取回审计。";
        }

        MessageBox.Show(
            this,
            string.IsNullOrWhiteSpace(details)
                ? _statusLabel.Text
                : $"{_statusLabel.Text}{Environment.NewLine}{Environment.NewLine}{details}",
            "CDSI Beacon",
            MessageBoxButtons.OK,
            result.Status == RestoreJobStatus.Cancelled
                ? MessageBoxIcon.Information
                : MessageBoxIcon.Warning);
    }
}
