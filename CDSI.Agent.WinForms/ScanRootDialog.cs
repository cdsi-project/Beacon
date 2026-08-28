using CDSI.Agent.Core.Assets;
using CDSI.Agent.Core.Scanning;

namespace CDSI.Agent.WinForms;

public sealed class ScanRootDialog : Form
{
    private static readonly IReadOnlyList<FileTypeChoice> FileTypeChoices =
    [
        new(AssetFileTypeFilter.Video, "视频"),
        new(AssetFileTypeFilter.Audio, "音频"),
        new(AssetFileTypeFilter.Image, "图片"),
        new(AssetFileTypeFilter.Document, "文档"),
        new(AssetFileTypeFilter.Other, "其他")
    ];

    private readonly TextBox _pathTextBox = new();
    private readonly Dictionary<AssetFileTypeFilter, CheckBox> _fileTypeCheckBoxes = [];
    private readonly CheckBox _customExtensionCheckBox = new();
    private readonly TextBox _extensionTextBox = new();
    private readonly ListBox _extensionListBox = new();
    private readonly Button _addExtensionButton = new();
    private readonly Button _removeExtensionButton = new();
    private readonly TableLayoutPanel _whitelistEditor = new();
    private readonly CheckBox _idleScanCheckBox = new();
    private readonly NumericUpDown _idleScanIntervalInput = new();
    private readonly ComboBox _idleScanUnitComboBox = new();
    private readonly bool _requireAvailablePath;

    public ScanRootDialog(
        string? path = null,
        IReadOnlyCollection<AssetFileTypeFilter>? fileTypeFilters = null,
        IReadOnlyCollection<string>? extensionWhitelist = null,
        bool allowPathSelection = true,
        IdleScanSchedule? idleScanSchedule = null)
    {
        var effectiveFileTypes = fileTypeFilters ??
            (extensionWhitelist?.Count > 0
                ? Array.Empty<AssetFileTypeFilter>()
                : ScanFileFilter.AllFileTypes);
        var filter = new ScanFileFilter(effectiveFileTypes, extensionWhitelist);
        var effectiveIdleSchedule = idleScanSchedule ?? IdleScanSchedule.Disabled;
        _requireAvailablePath = allowPathSelection;

        Text = allowPathSelection ? "添加扫描目录" : "设置扫描目录";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(680, 460);
        MinimumSize = new Size(600, 420);
        ShowInTaskbar = false;
        Font = new Font("Segoe UI", 9F);
        AutoScaleMode = AutoScaleMode.Dpi;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 7,
            Padding = new Padding(20, 18, 20, 16)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

        var directoryLabel = CreateLabel("扫描目录");
        layout.Controls.Add(directoryLabel, 0, 0);
        layout.SetColumnSpan(directoryLabel, 3);

        _pathTextBox.Dock = DockStyle.Fill;
        _pathTextBox.Margin = new Padding(0, 5, 8, 5);
        _pathTextBox.Text = path ?? string.Empty;
        _pathTextBox.ReadOnly = !allowPathSelection;
        _pathTextBox.AccessibleName = "扫描目录路径";
        layout.Controls.Add(_pathTextBox, 0, 1);
        layout.SetColumnSpan(_pathTextBox, 2);

        var browseButton = CreateButton(
            "浏览",
            Color.FromArgb(236, 239, 242),
            Color.FromArgb(31, 37, 43));
        browseButton.Enabled = allowPathSelection;
        browseButton.Margin = new Padding(4, 5, 0, 5);
        browseButton.Click += BrowseButton_Click;
        layout.Controls.Add(browseButton, 2, 1);

        var fileTypePanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty
        };
        var fileTypeLabel = CreateLabel("扫描策略");
        fileTypeLabel.Width = 84;
        fileTypeLabel.Margin = new Padding(0, 5, 0, 5);
        fileTypePanel.Controls.Add(fileTypeLabel);
        foreach (var choice in FileTypeChoices)
        {
            var checkBox = new CheckBox
            {
                Text = choice.Label,
                AutoSize = true,
                Checked = filter.FileTypeFilters.Contains(choice.Value),
                Margin = new Padding(8, 13, 4, 0),
                AccessibleName = $"扫描策略 {choice.Label}"
            };
            _fileTypeCheckBoxes.Add(choice.Value, checkBox);
            fileTypePanel.Controls.Add(checkBox);
        }

        _customExtensionCheckBox.Text = "自定义扩展名";
        _customExtensionCheckBox.AutoSize = true;
        _customExtensionCheckBox.Checked = filter.UsesExtensionWhitelist;
        _customExtensionCheckBox.Margin = new Padding(8, 13, 0, 0);
        _customExtensionCheckBox.AccessibleName = "扫描策略 自定义扩展名";
        _customExtensionCheckBox.CheckedChanged += (_, _) => UpdateWhitelistState();
        fileTypePanel.Controls.Add(_customExtensionCheckBox);
        layout.Controls.Add(fileTypePanel, 0, 2);
        layout.SetColumnSpan(fileTypePanel, 3);

        var whitelistLabel = CreateLabel("扩展名白名单");
        layout.Controls.Add(whitelistLabel, 0, 3);
        layout.SetColumnSpan(whitelistLabel, 3);

        _whitelistEditor.Dock = DockStyle.Fill;
        _whitelistEditor.ColumnCount = 2;
        _whitelistEditor.RowCount = 2;
        _whitelistEditor.Margin = Padding.Empty;
        _whitelistEditor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _whitelistEditor.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104));
        _whitelistEditor.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        _whitelistEditor.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _extensionTextBox.Dock = DockStyle.Fill;
        _extensionTextBox.Margin = new Padding(0, 5, 8, 5);
        _extensionTextBox.PlaceholderText = ".mp4";
        _extensionTextBox.AccessibleName = "白名单扩展名输入";
        _extensionTextBox.KeyDown += ExtensionTextBox_KeyDown;
        _whitelistEditor.Controls.Add(_extensionTextBox, 0, 0);

        _addExtensionButton.Text = "添加";
        _addExtensionButton.Dock = DockStyle.Fill;
        _addExtensionButton.Margin = new Padding(4, 5, 0, 5);
        _addExtensionButton.FlatStyle = FlatStyle.Flat;
        _addExtensionButton.Click += AddExtensionButton_Click;
        _whitelistEditor.Controls.Add(_addExtensionButton, 1, 0);

        _extensionListBox.Dock = DockStyle.Fill;
        _extensionListBox.IntegralHeight = false;
        _extensionListBox.AccessibleName = "扩展名白名单";
        _extensionListBox.SelectedIndexChanged += (_, _) =>
            _removeExtensionButton.Enabled = _extensionListBox.SelectedIndex >= 0;
        foreach (var extension in filter.ExtensionWhitelist)
        {
            _extensionListBox.Items.Add(extension);
        }

        _whitelistEditor.Controls.Add(_extensionListBox, 0, 1);

        _removeExtensionButton.Text = "移除";
        _removeExtensionButton.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _removeExtensionButton.Height = 32;
        _removeExtensionButton.Margin = new Padding(4, 0, 0, 0);
        _removeExtensionButton.FlatStyle = FlatStyle.Flat;
        _removeExtensionButton.Enabled = false;
        _removeExtensionButton.Click += RemoveExtensionButton_Click;
        _whitelistEditor.Controls.Add(_removeExtensionButton, 1, 1);

        layout.Controls.Add(_whitelistEditor, 0, 4);
        layout.SetColumnSpan(_whitelistEditor, 3);

        var idleScanPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = new Padding(0, 6, 0, 0)
        };
        _idleScanCheckBox.Text = "空闲时扫描";
        _idleScanCheckBox.AutoSize = true;
        _idleScanCheckBox.Checked = effectiveIdleSchedule.Enabled;
        _idleScanCheckBox.Margin = new Padding(0, 8, 18, 0);
        _idleScanCheckBox.AccessibleName = "空闲时扫描";
        _idleScanCheckBox.CheckedChanged += (_, _) => UpdateIdleScanState();
        idleScanPanel.Controls.Add(_idleScanCheckBox);

        var intervalLabel = CreateLabel("每");
        intervalLabel.AutoSize = true;
        intervalLabel.Margin = new Padding(0, 9, 6, 0);
        idleScanPanel.Controls.Add(intervalLabel);

        _idleScanIntervalInput.Minimum = IdleScanSchedule.MinimumInterval;
        _idleScanIntervalInput.Maximum = IdleScanSchedule.MaximumInterval;
        _idleScanIntervalInput.Value = effectiveIdleSchedule.Interval;
        _idleScanIntervalInput.Width = 76;
        _idleScanIntervalInput.Margin = new Padding(0, 4, 8, 0);
        _idleScanIntervalInput.AccessibleName = "空闲扫描间隔";
        idleScanPanel.Controls.Add(_idleScanIntervalInput);

        _idleScanUnitComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _idleScanUnitComboBox.Width = 88;
        _idleScanUnitComboBox.Margin = new Padding(0, 4, 0, 0);
        _idleScanUnitComboBox.AccessibleName = "空闲扫描时间单位";
        _idleScanUnitComboBox.Items.AddRange(IdleScanUnitChoice.All);
        _idleScanUnitComboBox.SelectedIndex = Array.FindIndex(
            IdleScanUnitChoice.All,
            choice => choice.Value == effectiveIdleSchedule.Unit);
        idleScanPanel.Controls.Add(_idleScanUnitComboBox);

        layout.Controls.Add(idleScanPanel, 0, 5);
        layout.SetColumnSpan(idleScanPanel, 3);

        var confirmButton = CreateButton(
            allowPathSelection ? "添加目录" : "保存",
            Color.FromArgb(24, 121, 78),
            Color.White);
        confirmButton.Size = new Size(96, 32);
        confirmButton.Click += ConfirmButton_Click;

        var cancelButton = CreateButton(
            "取消",
            Color.FromArgb(236, 239, 242),
            Color.FromArgb(31, 37, 43));
        cancelButton.Size = new Size(96, 32);
        cancelButton.DialogResult = DialogResult.Cancel;

        var commands = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = new Padding(0, 8, 0, 0)
        };
        commands.Controls.Add(confirmButton);
        commands.Controls.Add(cancelButton);
        layout.Controls.Add(commands, 0, 6);
        layout.SetColumnSpan(commands, 3);

        Controls.Add(layout);
        AcceptButton = confirmButton;
        CancelButton = cancelButton;
        UpdateWhitelistState();
        UpdateIdleScanState();
    }

    public string SelectedPath => Path.GetFullPath(_pathTextBox.Text.Trim());

    public IReadOnlyList<AssetFileTypeFilter> FileTypeFilters =>
        FileTypeChoices
            .Where(choice => _fileTypeCheckBoxes[choice.Value].Checked)
            .Select(choice => choice.Value)
            .ToArray();

    public IdleScanSchedule IdleScanSchedule => new(
        _idleScanCheckBox.Checked,
        decimal.ToInt32(_idleScanIntervalInput.Value),
        (_idleScanUnitComboBox.SelectedItem as IdleScanUnitChoice)?.Value ??
            IdleScanIntervalUnit.Hours);

    public IReadOnlyList<string> ExtensionWhitelist => IsWhitelistSelected
        ? _extensionListBox.Items.Cast<string>().ToArray()
        : Array.Empty<string>();

    internal bool IsWhitelistSelected =>
        _customExtensionCheckBox.Checked;

    private void BrowseButton_Click(object? sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "选择只读扫描目录",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false,
            InitialDirectory = Directory.Exists(_pathTextBox.Text)
                ? _pathTextBox.Text
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _pathTextBox.Text = dialog.SelectedPath;
        }
    }

    private void ExtensionTextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter)
        {
            return;
        }

        AddExtensions();
        e.Handled = true;
        e.SuppressKeyPress = true;
    }

    private void AddExtensionButton_Click(object? sender, EventArgs e)
    {
        AddExtensions();
    }

    private void AddExtensions()
    {
        try
        {
            var extensions = _extensionTextBox.Text.Split(
                [',', ';', ' ', '\t', '\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (extensions.Length == 0)
            {
                return;
            }

            AddExtensions(extensions);
            _extensionTextBox.Clear();
            _extensionTextBox.Focus();
        }
        catch (ArgumentException exception)
        {
            MessageBox.Show(
                this,
                exception.Message,
                "文件类型",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
    }

    internal void AddExtensions(IEnumerable<string> extensions)
    {
        foreach (var extension in ScanFileFilter.NormalizeExtensions(extensions))
        {
            if (!_extensionListBox.Items.Contains(extension))
            {
                _extensionListBox.Items.Add(extension);
            }
        }

        var sorted = _extensionListBox.Items.Cast<string>()
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _extensionListBox.Items.Clear();
        _extensionListBox.Items.AddRange(sorted);
    }

    private void RemoveExtensionButton_Click(object? sender, EventArgs e)
    {
        if (_extensionListBox.SelectedIndex >= 0)
        {
            _extensionListBox.Items.RemoveAt(_extensionListBox.SelectedIndex);
        }
    }

    private void ConfirmButton_Click(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_pathTextBox.Text) ||
            (_requireAvailablePath && !Directory.Exists(_pathTextBox.Text.Trim())))
        {
            MessageBox.Show(
                this,
                "请选择当前可用的扫描目录。",
                "CDSI Beacon",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        if (IsWhitelistSelected && _extensionListBox.Items.Count == 0)
        {
            MessageBox.Show(
                this,
                "请至少添加一个文件扩展名。",
                "文件类型",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        if (FileTypeFilters.Count == 0 && !IsWhitelistSelected)
        {
            MessageBox.Show(
                this,
                "请至少勾选一种扫描策略。",
                "扫描策略",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        DialogResult = DialogResult.OK;
        Close();
    }

    private void UpdateWhitelistState()
    {
        _whitelistEditor.Enabled = IsWhitelistSelected;
    }

    private void UpdateIdleScanState()
    {
        _idleScanIntervalInput.Enabled = _idleScanCheckBox.Checked;
        _idleScanUnitComboBox.Enabled = _idleScanCheckBox.Checked;
    }

    private static Label CreateLabel(string text)
    {
        return new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI Semibold", 9F),
            ForeColor = Color.FromArgb(52, 61, 69)
        };
    }

    private static Button CreateButton(string text, Color background, Color foreground)
    {
        return new Button
        {
            Text = text,
            BackColor = background,
            ForeColor = foreground,
            FlatStyle = FlatStyle.Flat,
            FlatAppearance = { BorderSize = 0 },
            Cursor = Cursors.Hand
        };
    }

    private sealed record FileTypeChoice(
        AssetFileTypeFilter Value,
        string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record IdleScanUnitChoice(
        IdleScanIntervalUnit Value,
        string Label)
    {
        public static IdleScanUnitChoice[] All { get; } =
        [
            new(IdleScanIntervalUnit.Minutes, "分钟"),
            new(IdleScanIntervalUnit.Hours, "小时"),
            new(IdleScanIntervalUnit.Days, "天")
        ];

        public override string ToString() => Label;
    }
}
