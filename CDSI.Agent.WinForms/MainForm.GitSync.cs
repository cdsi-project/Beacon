using CDSI.Agent.Application.Git;
using CDSI.Agent.Core.Collections;
using CDSI.Agent.Core.Git;

namespace CDSI.Agent.WinForms;

public sealed partial class MainForm
{
    private void ConfigureProjectGitMenu(
        ToolStripMenuItem menuItem,
        IReadOnlyList<ConfiguredGitProfile> profiles,
        bool canSync)
    {
        PopulateProjectGitMenu(menuItem, profiles, canSync);
        foreach (var profileItem in menuItem.DropDownItems
                     .OfType<ToolStripMenuItem>()
                     .Where(item => item.Tag is Guid))
        {
            profileItem.Click += SyncProjectToGitProfile_Click;
        }
    }

    internal static void PopulateProjectGitMenu(
        ToolStripMenuItem menuItem,
        IReadOnlyList<ConfiguredGitProfile> profiles,
        bool canSync)
    {
        ArgumentNullException.ThrowIfNull(menuItem);
        ArgumentNullException.ThrowIfNull(profiles);
        foreach (var existingItem in menuItem.DropDownItems
                     .Cast<ToolStripItem>()
                     .ToArray())
        {
            existingItem.Dispose();
        }

        menuItem.DropDownItems.Clear();
        menuItem.Text = "同步到Git";
        menuItem.Enabled = canSync;
        if (profiles.Count == 0)
        {
            menuItem.DropDownItems.Add(new ToolStripMenuItem("尚未配置 Git 仓库")
            {
                Enabled = false
            });
            return;
        }

        foreach (var configured in profiles)
        {
            var ready = GitProjectSyncService.IsProfileReady(configured);
            menuItem.DropDownItems.Add(new ToolStripMenuItem(
                ready
                    ? configured.Profile.DisplayName
                    : $"{configured.Profile.DisplayName}（凭据不可用）")
            {
                Tag = configured.Profile.Id,
                ToolTipText = configured.Profile.RepositoryUrl,
                Enabled = canSync && ready
            });
        }
    }

    internal static string CreateGitSyncConfirmation(GitProjectSyncPreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        var largeFileWarning = preview.LargeFileCount == 0
            ? string.Empty
            : $"{Environment.NewLine}{Environment.NewLine}" +
                $"其中 {preview.LargeFileCount:N0} 个文件不小于 50 MB；" +
                "远端可能拒绝大文件，Beacon 不会自动配置 Git LFS。";
        return
            $"确定将项目“{preview.ProjectName}”同步到 Git 仓库“{preview.ProfileName}”吗？" +
            $"{Environment.NewLine}{Environment.NewLine}" +
            $"仓库：{preview.RepositoryUrl}{Environment.NewLine}" +
            $"分支：{preview.Branch}{Environment.NewLine}" +
            $"资产：{preview.AssetCount:N0} 个 · {FormatFileSize(preview.TotalBytes)}" +
            largeFileWarning +
            $"{Environment.NewLine}{Environment.NewLine}" +
            "Beacon 将复制文件到临时工作副本并创建提交，不会修改、移动或删除原始资产。" +
            "仓库中未由 Beacon 管理的同名不同内容文件不会被覆盖。";
    }

    private async void SyncProjectToGitProfile_Click(object? sender, EventArgs e)
    {
        var projects = GetSelectedCollections();
        if (sender is not ToolStripMenuItem { Tag: Guid profileId } ||
            projects.Count != 1)
        {
            return;
        }

        var project = projects[0];
        await PrepareAndSyncProjectToGitAsync(project, profileId);
    }

    private async Task PrepareAndSyncProjectToGitAsync(
        AssetCollectionSummary project,
        Guid profileId)
    {
        try
        {
            var preview = await _gitProjectSyncService.PrepareAsync(
                project.Id,
                profileId);
            if (MessageBox.Show(
                    this,
                    CreateGitSyncConfirmation(preview),
                    "同步项目到 Git",
                    MessageBoxButtons.OKCancel,
                    MessageBoxIcon.Information,
                    MessageBoxDefaultButton.Button2) != DialogResult.OK)
            {
                return;
            }

            await SyncProjectToGitAsync(project, preview);
        }
        catch (Exception exception)
        {
            ShowError("无法准备 Git 同步", exception);
        }
    }

    private async Task SyncProjectToGitAsync(
        AssetCollectionSummary project,
        GitProjectSyncPreview preview)
    {
        _scanCancellation?.Dispose();
        _scanCancellation = new CancellationTokenSource();
        var progress = new Progress<GitProjectSyncProgress>(UpdateGitSyncProgress);
        SetBusy(true);
        _progressBar.MarqueeAnimationSpeed = 0;
        _progressBar.Style = ProgressBarStyle.Continuous;
        _progressBar.Minimum = 0;
        _progressBar.Maximum = 1_000;
        _progressBar.Value = 0;
        _statusLabel.Text = $"正在同步项目到 Git：{project.Name}";
        try
        {
            var result = await _gitProjectSyncService.SyncAsync(
                project.Id,
                preview.ProfileId,
                progress,
                _scanCancellation.Token);
            await RefreshGitProjectsAsync();
            _statusLabel.Text = result.CreatedCommit
                ? $"Git 同步完成：{project.Name}"
                : $"Git 同步完成：{project.Name}（内容未变化）";
            _runtimeLog.WriteInformation(
                $"Git 项目同步完成；ProjectId={project.Id:D}；" +
                $"ProfileId={preview.ProfileId:D}；Branch={result.Branch}；" +
                $"Commit={result.CommitId}");
            MessageBox.Show(
                this,
                $"项目已同步到“{preview.ProfileName}”。" +
                $"{Environment.NewLine}{Environment.NewLine}" +
                $"分支：{result.Branch}{Environment.NewLine}" +
                $"提交：{result.CommitId}" +
                (result.CreatedCommit
                    ? string.Empty
                    : $"{Environment.NewLine}项目内容与远端一致，未创建新提交。"),
                "CDSI Beacon",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (OperationCanceledException)
        {
            _statusLabel.Text = "Git 同步已取消";
        }
        catch (Exception exception)
        {
            _runtimeLog.WriteError(
                $"Git 项目同步失败；ProjectId={project.Id:D}；" +
                $"ProfileId={preview.ProfileId:D}",
                exception);
            ShowError("同步项目到 Git 失败", exception);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void UpdateGitSyncProgress(GitProjectSyncProgress progress)
    {
        var isCopying = string.Equals(
            progress.Stage,
            "正在复制项目文件",
            StringComparison.Ordinal);
        _progressBar.Style = isCopying
            ? ProgressBarStyle.Continuous
            : ProgressBarStyle.Marquee;
        _progressBar.MarqueeAnimationSpeed = isCopying ? 0 : 24;
        if (isCopying)
        {
            _progressBar.Value = progress.TotalBytes == 0
                ? 0
                : (int)Math.Clamp(
                    progress.ProcessedBytes * 1_000d / progress.TotalBytes,
                    0d,
                    1_000d);
        }

        _progressLabel.Text =
            $"{progress.Stage} · 文件 {progress.ProcessedFiles:N0}/{progress.TotalFiles:N0} · " +
            $"{FormatFileSize(progress.ProcessedBytes)}/{FormatFileSize(progress.TotalBytes)}";
        _currentPathLabel.Text = progress.CurrentPath ?? string.Empty;
    }
}
