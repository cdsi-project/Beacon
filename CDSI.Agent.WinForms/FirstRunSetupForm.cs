using CDSI.Agent.Application.Workspaces;

namespace CDSI.Agent.WinForms;

public sealed class FirstRunSetupForm : Form
{
    private readonly TextBox _pathTextBox = new();
    private readonly Button _confirmButton = new();

    public FirstRunSetupForm(string? initialPath = null)
    {
        Text = "设置 CDSI 工作目录";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(640, 210);
        MinimumSize = new Size(560, 210);
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        Font = new Font("Segoe UI", 9F);
        AutoScaleMode = AutoScaleMode.Dpi;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 4,
            Padding = new Padding(24),
            BackColor = Color.White
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));

        var title = new Label
        {
            Text = "CDSI 工作目录",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Semibold", 14F),
            ForeColor = Color.FromArgb(31, 37, 43)
        };
        layout.Controls.Add(title, 0, 0);
        layout.SetColumnSpan(title, 3);

        _pathTextBox.Dock = DockStyle.Fill;
        _pathTextBox.Margin = new Padding(0, 6, 8, 6);
        _pathTextBox.Text = initialPath ?? WorkspaceApplicationService.GetSuggestedDefaultPath();
        _pathTextBox.AccessibleName = "工作目录路径";
        layout.Controls.Add(_pathTextBox, 0, 1);
        layout.SetColumnSpan(_pathTextBox, 2);

        var browseButton = CreateButton("浏览", Color.FromArgb(236, 239, 242), Color.FromArgb(31, 37, 43));
        browseButton.Margin = new Padding(4, 6, 0, 6);
        browseButton.Click += BrowseButton_Click;
        layout.Controls.Add(browseButton, 2, 1);

        var note = new Label
        {
            Text = "建议选择非系统盘。数据库保存在 System\\Data；重装后选择原工作目录可继续使用记录。",
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(88, 98, 106),
            TextAlign = ContentAlignment.TopLeft,
            Padding = new Padding(0, 6, 0, 0)
        };
        layout.Controls.Add(note, 0, 2);
        layout.SetColumnSpan(note, 3);

        var cancelButton = CreateButton("取消", Color.FromArgb(236, 239, 242), Color.FromArgb(82, 91, 99));
        cancelButton.DialogResult = DialogResult.Cancel;
        cancelButton.Margin = new Padding(4, 0, 4, 0);
        layout.Controls.Add(cancelButton, 1, 3);

        _confirmButton.Text = "使用并继续";
        _confirmButton.DialogResult = DialogResult.OK;
        _confirmButton.BackColor = Color.FromArgb(24, 121, 78);
        _confirmButton.ForeColor = Color.White;
        _confirmButton.FlatStyle = FlatStyle.Flat;
        _confirmButton.FlatAppearance.BorderSize = 0;
        _confirmButton.Dock = DockStyle.Fill;
        _confirmButton.Margin = new Padding(4, 0, 0, 0);
        layout.Controls.Add(_confirmButton, 2, 3);

        AcceptButton = _confirmButton;
        CancelButton = cancelButton;
        Controls.Add(layout);
    }

    public string SelectedPath => _pathTextBox.Text.Trim();

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (DialogResult == DialogResult.OK &&
            string.IsNullOrWhiteSpace(SelectedPath))
        {
            MessageBox.Show(
                this,
                "请选择工作目录。",
                "CDSI Beacon",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            e.Cancel = true;
        }

        base.OnFormClosing(e);
    }

    private void BrowseButton_Click(object? sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "选择 CDSI 工作目录",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
            InitialDirectory = FindExistingDirectory(_pathTextBox.Text)
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _pathTextBox.Text = dialog.SelectedPath;
        }
    }

    private static string FindExistingDirectory(string path)
    {
        var current = string.IsNullOrWhiteSpace(path)
            ? null
            : new DirectoryInfo(Path.GetFullPath(path));
        while (current is not null && !current.Exists)
        {
            current = current.Parent;
        }

        return current?.FullName ??
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }

    private static Button CreateButton(string text, Color background, Color foreground)
    {
        return new Button
        {
            Text = text,
            BackColor = background,
            ForeColor = foreground,
            FlatStyle = FlatStyle.Flat,
            Dock = DockStyle.Fill,
            Cursor = Cursors.Hand
        };
    }
}
