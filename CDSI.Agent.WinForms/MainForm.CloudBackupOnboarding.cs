namespace CDSI.Agent.WinForms;

public sealed partial class MainForm
{
    private readonly Button _openAssetsForBackupButton = new();
    private TableLayoutPanel? _cloudBackupOnboardingPanel;
    private SplitContainer? _cloudBackupProjectSplit;
    private bool _cloudBackupsLoaded;

    private Control CreateCloudBackupListHost()
    {
        var host = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            BackColor = Color.White
        };
        _cloudBackupProjectSplit = CreateCloudBackupProjectSplit(
            _cloudBackupProjectGrid, _cloudBackupGrid);
        _cloudBackupOnboardingPanel = CreateOnboardingPanel(
            _openAssetsForBackupButton,
            "开始备份你的资产",
            "全部资产 → 选择资产 → 右键 → 同步到云端\n\n" +
            "首次备份：先在“设置 → 备份配置”中添加云端存储。\n" +
            "资产需加入项目；未分类时可选择“加入项目并备份”。",
            "前往全部资产",
            "云备份操作引导");
        _cloudBackupOnboardingPanel.Visible = false;
        _openAssetsForBackupButton.Click += (_, _) =>
            _mainTabControl.SelectedTab = _assetsTabPage;
        host.Controls.Add(_cloudBackupProjectSplit);
        host.Controls.Add(_cloudBackupOnboardingPanel);
        return host;
    }

    private void UpdateCloudBackupOnboarding()
    {
        if (_cloudBackupOnboardingPanel is null || _cloudBackupProjectSplit is null)
        {
            return;
        }

        var showOnboarding = _initializationSucceeded && _cloudBackupsLoaded &&
            _availableCloudBackups.Count == 0;
        _cloudBackupOnboardingPanel.Visible = showOnboarding;
        _cloudBackupProjectSplit.Visible = !showOnboarding;
        _openAssetsForBackupButton.Enabled = showOnboarding && !_isBusy;
        if (showOnboarding)
        {
            _cloudBackupOnboardingPanel.BringToFront();
        }
    }
}
