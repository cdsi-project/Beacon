using System.Diagnostics;
using CDSI.Agent.Application.OpenWeb;
using CDSI.Agent.Core.Assets;
using CDSI.Agent.Core.OpenWeb;

namespace CDSI.Agent.WinForms;

public sealed partial class MainForm
{
    private readonly ToolStripMenuItem _publishToOpenWebMenuItem = new();

    private async Task PublishSelectedArticleAsync()
    {
        var selected = GetSelectedAssets();
        if (selected.Count != 1)
        {
            return;
        }

        var asset = selected[0];
        if (asset.LocationStatus != AssetLocationStatus.Available ||
            !_openWebPublishingService.Supports(asset.Path))
        {
            MessageBox.Show(
                this,
                "当前只支持发布位置可用的 Markdown 或 TXT 文章。",
                "CDSI Beacon",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        if (!TryBeginStatefulOperation())
        {
            return;
        }

        _progressBar.Style = ProgressBarStyle.Marquee;
        _progressBar.MarqueeAnimationSpeed = 24;
        _progressLabel.Text = "正在准备文章发布";
        _currentPathLabel.Text = asset.Path;
        _statusLabel.Text = "正在准备发布到 OpenWeb";

        try
        {
            var defaultTitle = Path.GetFileNameWithoutExtension(asset.OriginalFilename);
            IReadOnlyList<ConfiguredOpenWebSource> sources;
            try
            {
                sources = await _openWebSettingsService.ListAsync();
            }
            catch (Exception exception)
            {
                ShowError("无法读取 OpenWeb 源站", exception);
                return;
            }

            if (sources.Count == 0)
            {
                MessageBox.Show(
                    this,
                    "尚未配置 OpenWeb 源站，请先在设置中添加。",
                    "CDSI Beacon",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            using var confirmation = new OpenWebArticlePublishForm(
                defaultTitle,
                asset.Path,
                sources);
            if (confirmation.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            _scanCancellation?.Dispose();
            _scanCancellation = new CancellationTokenSource();
            _progressLabel.Text = "正在发布文章";
            _statusLabel.Text = "正在发布到 OpenWeb";

            try
            {
                var result = await _openWebPublishingService.PublishAsync(
                    new OpenWebArticlePublishRequest(
                        asset.AssetId,
                        confirmation.SourceId,
                        asset.Path,
                        confirmation.ArticleTitle,
                        confirmation.ArticleStatus),
                    _scanCancellation.Token);
                ShowOpenWebPublishResult(result);
            }
            catch (OperationCanceledException)
            {
                _statusLabel.Text = "OpenWeb 发布已取消";
            }
            catch (Exception exception)
            {
                _statusLabel.Text = "OpenWeb 发布失败";
                ShowError("文章未能发布到 OpenWeb", exception);
            }
        }
        finally
        {
            _progressBar.MarqueeAnimationSpeed = 0;
            _progressBar.Style = ProgressBarStyle.Blocks;
            SetBusy(false);
        }
    }

    private void ShowOpenWebPublishResult(OpenWebArticlePublishResult result)
    {
        var action = result.WasCreated ? "创建" : "更新";
        var status = result.Publication.Status == OpenWebArticleStatus.Published
            ? "已发布"
            : "已保存为草稿";
        _statusLabel.Text = $"OpenWeb 文章{action}完成 · {status}";

        var buttons = result.Publication.Status == OpenWebArticleStatus.Published
            ? MessageBoxButtons.YesNo
            : MessageBoxButtons.OK;
        var message = result.Publication.Status == OpenWebArticleStatus.Published
            ? $"文章已{action}并发布。{Environment.NewLine}{Environment.NewLine}{result.Publication.RemoteUrl}{Environment.NewLine}{Environment.NewLine}现在打开文章？"
            : $"文章草稿已{action}。{Environment.NewLine}{Environment.NewLine}WordPress 文章 ID：{result.Publication.RemotePostId}";
        var dialogResult = MessageBox.Show(
            this,
            message,
            "CDSI Beacon",
            buttons,
            MessageBoxIcon.Information);
        if (dialogResult == DialogResult.Yes)
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = result.Publication.RemoteUrl,
                    UseShellExecute = true
                });
            }
            catch (Exception exception)
            {
                ShowError("文章已发布，但无法打开线上地址", exception);
            }
        }
    }
}
