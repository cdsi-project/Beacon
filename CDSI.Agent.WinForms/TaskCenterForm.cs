namespace CDSI.Agent.WinForms;

internal sealed class TaskCenterForm : Form
{
    private readonly Func<TaskCenterSnapshot> _snapshotProvider;
    private readonly Action _cancelAction;
    private readonly Label _statusValueLabel = new();
    private readonly Label _progressValueLabel = new();
    private readonly Label _pathValueLabel = new();
    private readonly Label _databaseValueLabel = new();
    private readonly ProgressBar _progressBar = new();
    private readonly Button _cancelButton = new();
    private readonly System.Windows.Forms.Timer _refreshTimer = new();

    public TaskCenterForm(
        Func<TaskCenterSnapshot> snapshotProvider,
        Action cancelAction)
    {
        _snapshotProvider = snapshotProvider;
        _cancelAction = cancelAction;
        Text = "任务中心";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(620, 310);
        MinimumSize = new Size(520, 290);
        ShowInTaskbar = false;
        Font = new Font("Segoe UI", 9F);
        AutoScaleMode = AutoScaleMode.Dpi;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            Padding = new Padding(20),
            BackColor = Color.White
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(CreateHeading("当前任务"), 0, 0);
        layout.Controls.Add(CreateValueLabel(_statusValueLabel, "任务状态"), 0, 1);
        _progressBar.Dock = DockStyle.Fill;
        _progressBar.Margin = new Padding(0, 8, 0, 8);
        layout.Controls.Add(_progressBar, 0, 2);
        layout.Controls.Add(CreateValueLabel(_progressValueLabel, "任务进度"), 0, 3);
        layout.Controls.Add(CreateValueLabel(_pathValueLabel, "当前处理路径"), 0, 4);

        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Margin = Padding.Empty,
            Padding = new Padding(0, 8, 0, 0)
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
        _databaseValueLabel.Dock = DockStyle.Fill;
        _databaseValueLabel.AutoEllipsis = true;
        _databaseValueLabel.TextAlign = ContentAlignment.MiddleLeft;
        _databaseValueLabel.ForeColor = Color.FromArgb(112, 121, 129);
        _cancelButton.Dock = DockStyle.Fill;
        _cancelButton.Text = "取消当前任务";
        _cancelButton.Click += (_, _) => _cancelAction();
        footer.Controls.Add(_databaseValueLabel, 0, 0);
        footer.Controls.Add(_cancelButton, 1, 0);
        layout.Controls.Add(footer, 0, 5);
        Controls.Add(layout);

        _refreshTimer.Interval = 500;
        _refreshTimer.Tick += (_, _) => RefreshSnapshot();
        Shown += (_, _) =>
        {
            RefreshSnapshot();
            _refreshTimer.Start();
        };
        FormClosed += (_, _) =>
        {
            _refreshTimer.Stop();
            _refreshTimer.Dispose();
        };
    }

    internal void ApplySnapshot(TaskCenterSnapshot snapshot)
    {
        _statusValueLabel.Text = snapshot.Status;
        _progressValueLabel.Text = snapshot.Progress;
        _pathValueLabel.Text = snapshot.CurrentPath;
        _databaseValueLabel.Text = snapshot.DatabaseStatus;
        _cancelButton.Enabled = snapshot.CanCancel;
        _progressBar.Style = snapshot.IsIndeterminate
            ? ProgressBarStyle.Marquee
            : ProgressBarStyle.Continuous;
        _progressBar.MarqueeAnimationSpeed = snapshot.IsIndeterminate ? 24 : 0;
        if (!snapshot.IsIndeterminate)
        {
            _progressBar.Value = Math.Clamp(snapshot.ProgressPercent ?? 0, 0, 100);
        }
    }

    internal string StatusText => _statusValueLabel.Text;

    internal string ProgressText => _progressValueLabel.Text;

    internal string CurrentPathText => _pathValueLabel.Text;

    internal bool CanCancel => _cancelButton.Enabled;

    internal ProgressBarStyle CurrentProgressStyle => _progressBar.Style;

    internal int CurrentProgressValue => _progressBar.Value;

    private void RefreshSnapshot()
    {
        ApplySnapshot(_snapshotProvider());
    }

    private static Label CreateHeading(string text)
    {
        return new Label
        {
            Dock = DockStyle.Fill,
            Text = text,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI Semibold", 12F),
            ForeColor = Color.FromArgb(31, 37, 43)
        };
    }

    private static Label CreateValueLabel(Label label, string accessibleName)
    {
        label.Dock = DockStyle.Fill;
        label.AutoEllipsis = true;
        label.TextAlign = ContentAlignment.MiddleLeft;
        label.ForeColor = Color.FromArgb(52, 61, 69);
        label.AccessibleName = accessibleName;
        return label;
    }
}

internal sealed record TaskCenterSnapshot(
    string Status,
    string Progress,
    string CurrentPath,
    int? ProgressPercent,
    bool IsIndeterminate,
    bool CanCancel,
    string DatabaseStatus);
