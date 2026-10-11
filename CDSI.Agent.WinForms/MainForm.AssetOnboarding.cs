namespace CDSI.Agent.WinForms;

public sealed partial class MainForm
{
    private readonly Button _assetOnboardingButton = new();
    private TableLayoutPanel? _assetOnboardingPanel;
    private long? _assetOnboardingTotalCount;

    private Control CreateAssetListHost()
    {
        var host = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            BackColor = Color.White
        };
        _assetOnboardingPanel = CreateAssetOnboardingPanel(_assetOnboardingButton);
        _assetOnboardingPanel.Visible = false;
        _assetOnboardingButton.Click += async (_, _) =>
        {
            if (_initializationSucceeded && !_isBusy)
            {
                await OpenSettingsAsync(openScanRoots: true);
            }
        };
        host.Controls.Add(_assetGrid);
        host.Controls.Add(_assetOnboardingPanel);
        return host;
    }

    internal static TableLayoutPanel CreateAssetOnboardingPanel(Button setupButton)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = Padding.Empty,
            BackColor = Color.White,
            AccessibleName = "资产扫描引导"
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

        var content = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Anchor = AnchorStyles.None,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(16),
            Margin = Padding.Empty
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        for (var row = 0; row < 3; row++)
        {
            content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }
        content.Controls.Add(new Label
        {
            AutoSize = true,
            Anchor = AnchorStyles.None,
            Text = "开始扫描并登记资产",
            Font = new Font("Segoe UI Semibold", 16F),
            ForeColor = Color.FromArgb(31, 37, 43),
            Margin = new Padding(0, 0, 0, 12)
        }, 0, 0);
        content.Controls.Add(new Label
        {
            AutoSize = true,
            Anchor = AnchorStyles.None,
            Text = "先选择素材所在目录和扫描类型，再点击“开始扫描”。",
            ForeColor = Color.FromArgb(88, 98, 106),
            Margin = new Padding(0, 0, 0, 20)
        }, 0, 1);
        setupButton.Text = "设置扫描目录";
        setupButton.AccessibleName = "设置扫描目录并开始登记资产";
        setupButton.AutoSize = true;
        setupButton.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        setupButton.Anchor = AnchorStyles.None;
        setupButton.Padding = new Padding(22, 10, 22, 10);
        setupButton.Margin = Padding.Empty;
        setupButton.FlatStyle = FlatStyle.Flat;
        setupButton.FlatAppearance.BorderSize = 0;
        setupButton.BackColor = Color.FromArgb(24, 121, 78);
        setupButton.ForeColor = Color.White;
        setupButton.Cursor = Cursors.Hand;
        setupButton.Enabled = false;
        content.Controls.Add(setupButton, 0, 2);
        panel.Controls.Add(content, 0, 1);
        return panel;
    }

    private void UpdateAssetOnboarding()
    {
        if (_assetOnboardingPanel is null)
        {
            return;
        }

        var showOnboarding = _initializationSucceeded && _assetOnboardingTotalCount == 0;
        _assetOnboardingPanel.Visible = showOnboarding;
        _assetGrid.Visible = !showOnboarding;
        _assetOnboardingButton.Enabled = showOnboarding && !_isBusy;
        if (showOnboarding)
        {
            _assetOnboardingPanel.BringToFront();
        }
    }
}
