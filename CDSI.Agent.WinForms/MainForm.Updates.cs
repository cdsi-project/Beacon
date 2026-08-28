using System.Diagnostics;

namespace CDSI.Agent.WinForms;

public sealed partial class MainForm
{
    private bool _isCheckingForUpdates;

    private async Task CheckForUpdatesAsync(bool showCurrentStatus)
    {
        if (_isCheckingForUpdates)
        {
            if (showCurrentStatus)
            {
                MessageBox.Show(
                    this,
                    "正在检查新版本，请稍候。",
                    "检查更新",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }

            return;
        }

        _isCheckingForUpdates = true;
        _checkForUpdatesMenuItem.Enabled = false;
        try
        {
            var result = await _applicationUpdateChecker.CheckAsync(
                GetApplicationVersion());
            _runtimeLog.WriteInformation(
                $"Gitee 版本检查完成；本地={result.CurrentVersion}；" +
                $"远端={result.LatestVersion}；发现更新={result.IsUpdateAvailable}");

            if (IsDisposed || Disposing)
            {
                return;
            }

            if (result.IsUpdateAvailable)
            {
                var choice = MessageBox.Show(
                    this,
                    $"发现新版本 v{result.LatestVersion}。\n\n" +
                    $"当前版本：v{result.CurrentVersion}\n\n" +
                    "是否打开 Gitee 发布页面？",
                    "CDSI Beacon 更新",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information);
                if (choice == DialogResult.Yes)
                {
                    using var process = Process.Start(
                        CreateOpenGiteeReleasesStartInfo());
                }

                return;
            }

            if (showCurrentStatus)
            {
                MessageBox.Show(
                    this,
                    $"当前已经是最新版本。\n\n" +
                    $"当前版本：v{result.CurrentVersion}\n" +
                    $"Gitee 版本：v{result.LatestVersion}",
                    "检查更新",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }
        catch (Exception exception)
        {
            if (showCurrentStatus && !IsDisposed && !Disposing)
            {
                ShowError("无法检查更新", exception);
            }
            else
            {
                _runtimeLog.WriteError("自动检查 Gitee 新版本失败", exception);
            }
        }
        finally
        {
            _isCheckingForUpdates = false;
            if (!IsDisposed && !Disposing)
            {
                _checkForUpdatesMenuItem.Enabled = true;
            }
        }
    }

    internal static ProcessStartInfo CreateOpenGiteeReleasesStartInfo()
    {
        return new ProcessStartInfo(GiteeApplicationUpdateChecker.ReleasesUrl)
        {
            UseShellExecute = true
        };
    }
}
