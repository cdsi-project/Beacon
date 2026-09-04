using CDSI.Agent.Application.Storage;
using CDSI.Agent.Core.Assets;
using CDSI.Agent.Core.Storage;

namespace CDSI.Agent.WinForms;

public sealed partial class MainForm
{
    private readonly TransferSpeedTracker _backupSpeedTracker = new();

    private async Task SyncSelectedAssetsToProjectAsync()
    {
        var selected = GetSelectedAssets();
        if (selected.Count == 0)
        {
            return;
        }

        var commonProjects = FindCommonProjects(_availableCollections, selected);
        if (commonProjects.Count == 0)
        {
            await AddSelectedAssetsToProjectAndSyncAsync();
            return;
        }

        Guid? projectId;
        if (commonProjects.Count == 1)
        {
            projectId = commonProjects[0].Id;
        }
        else
        {
            using var selection = new AssetCollectionSelectionForm(
                commonProjects,
                selected.Count,
                AssetCollectionSelectionPurpose.Sync);
            projectId = selection.ShowDialog(this) == DialogResult.OK
                ? selection.SelectedCollectionId
                : null;
        }

        if (projectId is not null)
        {
            await SyncSelectedAssetsToProjectAsync(projectId.Value);
        }
    }

    private async Task SyncSelectedAssetsToProjectAsync(Guid projectId)
    {
        var selected = GetSelectedAssets();
        if (selected.Count == 0)
        {
            return;
        }

        await SyncAssetsToProjectAsync(
            projectId,
            selected.Select(asset => asset.AssetId).ToArray());
    }

    private async Task AddSelectedAssetsToProjectAndSyncAsync()
    {
        var selected = GetSelectedAssets();
        if (selected.Count == 0)
        {
            return;
        }

        if (!TryBeginStatefulOperation())
        {
            return;
        }

        try
        {
            var projects = await _assetCollectionService.ListAsync();
            Guid? projectId;
            if (projects.Count == 0)
            {
                projectId = await CreateCollectionWithDialogAsync();
            }
            else
            {
                using var selection = new AssetCollectionSelectionForm(
                    projects,
                    selected.Count,
                    AssetCollectionSelectionPurpose.AddAndSync);
                if (selection.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                projectId = selection.CreateNewProject
                    ? await CreateCollectionWithDialogAsync()
                    : selection.SelectedCollectionId;
            }

            if (projectId is null)
            {
                return;
            }

            var assetIds = selected
                .Select(asset => asset.AssetId)
                .Distinct()
                .ToArray();
            if (!await _stateDatabaseWriteGate.TryRunAsync(async () =>
            {
                var added = await _assetCollectionService.AddAssetsAsync(
                    projectId.Value,
                    assetIds);
                await RefreshAssetCollectionsAsync(projectId);
                await RefreshAssetPageAsync();
                _statusLabel.Text = added == 0
                    ? "所选资产已在目标项目中，准备同步"
                    : $"已将 {added:N0} 个资产加入项目，准备同步";
            }))
            {
                return;
            }

            await SyncAssetsToProjectCoreAsync(projectId.Value, assetIds);
        }
        catch (Exception exception)
        {
            ShowError("无法加入项目并同步到云端", exception);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task SyncAssetsToProjectAsync(
        Guid projectId,
        IReadOnlyCollection<Guid> assetIds)
    {
        if (!TryBeginStatefulOperation())
        {
            return;
        }

        try
        {
            await SyncAssetsToProjectCoreAsync(projectId, assetIds);
        }
        catch (Exception exception)
        {
            ShowError("无法同步项目资产到云端", exception);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task SyncAssetsToProjectCoreAsync(
        Guid projectId,
        IReadOnlyCollection<Guid> assetIds)
    {
        var plan = await _assetCollectionService.PrepareSelectedSyncAsync(
            projectId,
            assetIds);
        await BackupProjectAssetsAsync(
            plan.Assets,
            $"正在同步项目：{plan.Collection.Name}",
            plan.Collection.Name,
            plan.Collection.BackupProfileIds);
    }

    private async Task BackupProjectAssetsAsync(
        IReadOnlyCollection<AssetListItem> assets,
        string progressStatus,
        string projectDirectory,
        IReadOnlyCollection<Guid> backupProfileIds)
    {
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(backupProfileIds);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);
        var boundProfileIds = backupProfileIds.Distinct().ToArray();

        if (assets.Any(asset =>
                asset.LocationStatus != AssetLocationStatus.Available))
        {
            MessageBox.Show(
                this,
                "选择中包含不可用的本地位置，请重新选择可用文件。",
                "CDSI Beacon",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        IReadOnlyList<ConfiguredObjectStorageProfile> profiles;
        try
        {
            var configuredProfiles = await _storageService.ListAsync();
            profiles = SelectBackupProfiles(configuredProfiles, boundProfileIds);
        }
        catch (Exception exception)
        {
            ShowError("无法读取备份配置", exception);
            return;
        }

        if (boundProfileIds.Length > 0 && profiles.Count != boundProfileIds.Length)
        {
            MessageBox.Show(
                this,
                $"该项目绑定的 {boundProfileIds.Length:N0} 个云端备份配置中，有 " +
                $"{boundProfileIds.Length - profiles.Count:N0} 个不存在或缺少有效凭据。" +
                "请检查“设置”的“备份配置”。",
                "CDSI Beacon",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        if (profiles.Count == 0)
        {
            MessageBox.Show(
                this,
                "尚未配置带有效凭据的备份存储。请先在“设置”的“备份配置”中添加配置。",
                "CDSI Beacon",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        var uniqueAssets = assets
            .GroupBy(asset => asset.AssetId)
            .Select(group => group.First())
            .ToArray();
        using var confirmation = new OssBackupConfirmationForm(
            profiles,
            uniqueAssets,
            projectDirectory,
            useAllProfiles: boundProfileIds.Length > 0);
        if (confirmation.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        _scanCancellation?.Dispose();
        _scanCancellation = new CancellationTokenSource();
        var backupProgress =
            new Progress<ObjectStorageBackupProgress>(UpdateBackupProgress);

        _progressBar.MarqueeAnimationSpeed = 0;
        _progressBar.Style = ProgressBarStyle.Continuous;
        _progressBar.Minimum = 0;
        _progressBar.Maximum = 1_000;
        _progressBar.Value = 0;
        _statusLabel.Text = progressStatus;

        try
        {
            var requests = uniqueAssets.Select(asset =>
                new ObjectStorageBackupRequest(
                    asset.AssetId,
                    asset.Path,
                    confirmation.SelectedObjectNames[asset.AssetId],
                    ObjectDirectory: projectDirectory))
                .ToArray();
            var selectedProfiles = confirmation.SelectedProfileIds
                .Select(profileId => profiles.Single(profile =>
                    profile.Profile.Id == profileId))
                .ToArray();
            var results = new List<(
                ConfiguredObjectStorageProfile Profile,
                ObjectStorageBackupResult Result)>();
            var targetErrors = new List<(
                ConfiguredObjectStorageProfile Profile,
                string ErrorMessage)>();
            for (var index = 0; index < selectedProfiles.Length; index++)
            {
                var profile = selectedProfiles[index];
                _backupSpeedTracker.Reset();
                _statusLabel.Text = selectedProfiles.Length == 1
                    ? progressStatus
                    : $"{progressStatus} · 目标 {index + 1:N0}/{selectedProfiles.Length:N0}：" +
                        profile.Profile.DisplayName;
                try
                {
                    var result = await _objectStorageBackupService.BackupAsync(
                        requests,
                        profile.Profile.Id,
                        backupProgress,
                        _scanCancellation.Token);
                    results.Add((profile, result));
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception) when (selectedProfiles.Length > 1)
                {
                    _runtimeLog.WriteError(
                        $"云端备份目标失败：{profile.Profile.DisplayName}",
                        exception);
                    targetErrors.Add((profile, exception.Message));
                }
            }

            await RefreshAssetPageAsync();
            if (results.Count == 1 && targetErrors.Count == 0)
            {
                ShowBackupResult(results[0].Result);
            }
            else
            {
                ShowMultipleBackupResults(
                    results,
                    targetErrors,
                    selectedProfiles.Length);
            }
        }
        catch (OperationCanceledException)
        {
            _statusLabel.Text = "云端备份已取消";
        }
        catch (Exception exception)
        {
            _statusLabel.Text = "云端备份失败";
            ShowError("云端备份未能完成", exception);
        }
        finally
        {
            _progressBar.Style = ProgressBarStyle.Blocks;
        }
    }

    internal static IReadOnlyList<ConfiguredObjectStorageProfile> SelectBackupProfiles(
        IReadOnlyList<ConfiguredObjectStorageProfile> configuredProfiles,
        IReadOnlyCollection<Guid> backupProfileIds)
    {
        ArgumentNullException.ThrowIfNull(configuredProfiles);
        ArgumentNullException.ThrowIfNull(backupProfileIds);
        var selectedIds = backupProfileIds.ToHashSet();
        return configuredProfiles
            .Where(profile =>
                profile.HasStoredSecret &&
                (selectedIds.Count == 0 || selectedIds.Contains(profile.Profile.Id)))
            .ToArray();
    }

    private void ShowMultipleBackupResults(
        IReadOnlyList<(
            ConfiguredObjectStorageProfile Profile,
            ObjectStorageBackupResult Result)> results,
        IReadOnlyList<(
            ConfiguredObjectStorageProfile Profile,
            string ErrorMessage)> targetErrors,
        int totalTargets)
    {
        var completedTargets = results.Count(item =>
            item.Result.Status == UploadJobStatus.Completed);
        var failedItems = results.Sum(item => item.Result.FailedItems);
        _statusLabel.Text = completedTargets == totalTargets
            ? $"云端备份完成，共 {totalTargets:N0} 个目标"
            : $"云端备份完成 {completedTargets:N0}/{totalTargets:N0} 个目标，" +
                $"{totalTargets - completedTargets:N0} 个目标未完成" +
                (failedItems > 0 ? $"，{failedItems:N0} 个文件失败" : string.Empty);
        var detailLines = results.Select(item =>
                $"{item.Profile.Profile.DisplayName}：" +
                $"完成 {item.Result.CompletedItems:N0} 个，" +
                $"失败 {item.Result.FailedItems:N0} 个")
            .Concat(targetErrors.Select(item =>
                $"{item.Profile.Profile.DisplayName}：失败 · {item.ErrorMessage}"));
        var details = string.Join(Environment.NewLine, detailLines);
        MessageBox.Show(
            this,
            $"{_statusLabel.Text}{Environment.NewLine}{Environment.NewLine}{details}",
            "CDSI Beacon",
            MessageBoxButtons.OK,
            completedTargets == totalTargets
                ? MessageBoxIcon.Information
                : MessageBoxIcon.Warning);
    }

    private void UpdateBackupProgress(ObjectStorageBackupProgress progress)
    {
        var bytesPerSecond = _backupSpeedTracker.Update(progress.NetworkTransferredBytes);
        var speedText = bytesPerSecond <= 0
            ? "--"
            : $"{FormatFileSize((long)bytesPerSecond)}/s";
        _progressLabel.Text =
            $"文件 {progress.ProcessedItems:N0}/{progress.TotalItems:N0} · {FormatFileSize(progress.UploadedBytes)}/{FormatFileSize(progress.TotalBytes)} · 速度 {speedText}";
        _currentPathLabel.Text = progress.Message is null
            ? progress.CurrentPath ?? string.Empty
            : $"{progress.Message} · {progress.CurrentPath}";
        _progressBar.Value = progress.TotalBytes == 0
            ? 0
            : (int)Math.Clamp(
                progress.UploadedBytes * 1_000d / progress.TotalBytes,
                0d,
                1_000d);
    }

    private void ShowBackupResult(ObjectStorageBackupResult result)
    {
        _statusLabel.Text = result.Status switch
        {
            UploadJobStatus.Completed =>
                $"云端备份完成，共 {result.CompletedItems:N0} 个资产",
            UploadJobStatus.Cancelled =>
                $"云端备份已取消，已完成 {result.CompletedItems:N0} 个资产",
            _ =>
                $"云端备份完成 {result.CompletedItems:N0} 个，失败 {result.FailedItems:N0} 个"
        };

        if (result.Status == UploadJobStatus.Completed)
        {
            MessageBox.Show(
                this,
                $"备份和完整性校验完成，共处理 {result.CompletedItems:N0} 个资产。",
                "CDSI Beacon",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        var errorLines = result.Items
            .Where(item => !string.IsNullOrWhiteSpace(item.ErrorMessage))
            .Take(8)
            .Select(item =>
                $"{item.SourcePath}{Environment.NewLine}{item.ErrorMessage}")
            .ToArray();
        var remaining = result.Items.Count(item =>
            !string.IsNullOrWhiteSpace(item.ErrorMessage)) - errorLines.Length;
        var details = string.Join(
            Environment.NewLine + Environment.NewLine,
            errorLines);
        if (remaining > 0)
        {
            details +=
                $"{Environment.NewLine}{Environment.NewLine}另有 {remaining:N0} 个错误，详情已写入本地上传审计。";
        }

        MessageBox.Show(
            this,
            string.IsNullOrWhiteSpace(details)
                ? _statusLabel.Text
                : $"{_statusLabel.Text}{Environment.NewLine}{Environment.NewLine}{details}",
            "CDSI Beacon",
            MessageBoxButtons.OK,
            result.Status == UploadJobStatus.Cancelled
                ? MessageBoxIcon.Information
                : MessageBoxIcon.Warning);
    }
}
