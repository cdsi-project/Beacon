using CDSI.Agent.Core.Reader;

namespace CDSI.Agent.WinForms;

internal sealed class ReaderSubscriptionDialog : Form
{
    private readonly TextBox _feedUrlTextBox = new();
    private readonly TextBox _titleTextBox = new();
    private readonly TextBox _folderTextBox = new();
    private readonly CheckBox _allowPrivateNetworkCheckBox = new();
    private readonly Button _okButton = new();

    public ReaderSubscriptionDialog()
    {
        Text = "添加 RSS 订阅";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(620, 285);
        MinimumSize = new Size(540, 270);
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        Font = new Font("Segoe UI", 9F);
        AutoScaleMode = AutoScaleMode.Dpi;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 5,
            Padding = new Padding(20),
            BackColor = Color.White
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        ConfigureTextBox(_feedUrlTextBox, "Feed URL");
        ConfigureTextBox(_titleTextBox, "订阅显示名称");
        ConfigureTextBox(_folderTextBox, "订阅文件夹");
        layout.Controls.Add(CreateLabel("Feed URL"), 0, 0);
        layout.Controls.Add(_feedUrlTextBox, 1, 0);
        layout.Controls.Add(CreateLabel("显示名称"), 0, 1);
        layout.Controls.Add(_titleTextBox, 1, 1);
        layout.Controls.Add(CreateLabel("文件夹"), 0, 2);
        layout.Controls.Add(_folderTextBox, 1, 2);

        _allowPrivateNetworkCheckBox.AutoSize = true;
        _allowPrivateNetworkCheckBox.Text = "允许访问本机或局域网地址";
        _allowPrivateNetworkCheckBox.AccessibleName = "允许局域网 Feed";
        _allowPrivateNetworkCheckBox.Margin = new Padding(0, 8, 0, 0);
        layout.Controls.Add(_allowPrivateNetworkCheckBox, 1, 3);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = new Padding(0, 8, 0, 0)
        };
        _okButton.Text = "添加";
        _okButton.Size = new Size(92, 32);
        _okButton.Click += OkButton_Click;
        var cancelButton = new Button
        {
            Text = "取消",
            DialogResult = DialogResult.Cancel,
            Size = new Size(92, 32)
        };
        buttons.Controls.Add(_okButton);
        buttons.Controls.Add(cancelButton);
        layout.Controls.Add(buttons, 0, 4);
        layout.SetColumnSpan(buttons, 2);
        Controls.Add(layout);
        AcceptButton = _okButton;
        CancelButton = cancelButton;
        Shown += (_, _) => _feedUrlTextBox.Focus();
    }

    public string FeedUrl => _feedUrlTextBox.Text.Trim();

    public string? PreferredTitle => Optional(_titleTextBox.Text);

    public string? FolderName => Optional(_folderTextBox.Text);

    public bool AllowPrivateNetwork => _allowPrivateNetworkCheckBox.Checked;

    private void OkButton_Click(object? sender, EventArgs e)
    {
        try
        {
            ReaderUrl.ParseAndNormalize(FeedUrl);
            DialogResult = DialogResult.OK;
        }
        catch (ArgumentException exception)
        {
            MessageBox.Show(
                this,
                exception.Message,
                "CDSI Beacon",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            _feedUrlTextBox.Focus();
            _feedUrlTextBox.SelectAll();
        }
    }

    private static Label CreateLabel(string text)
    {
        return new Label
        {
            Dock = DockStyle.Fill,
            Text = text,
            TextAlign = ContentAlignment.MiddleLeft
        };
    }

    private static void ConfigureTextBox(TextBox textBox, string accessibleName)
    {
        textBox.Dock = DockStyle.Fill;
        textBox.Margin = new Padding(0, 6, 0, 6);
        textBox.AccessibleName = accessibleName;
    }

    private static string? Optional(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
