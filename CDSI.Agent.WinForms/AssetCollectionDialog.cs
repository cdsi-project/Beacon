using CDSI.Agent.Application.Storage;
using CDSI.Agent.Core.Collections;
using CDSI.Agent.Core.Storage;

namespace CDSI.Agent.WinForms;

internal sealed class AssetCollectionDialog : Form
{
    private readonly TextBox _nameTextBox = new();
    private readonly ComboBox _typeComboBox = new();
    private readonly CheckBox _enableCloudBackupCheckBox = new();
    private readonly CheckedListBox _backupProfileListBox = new();

    public AssetCollectionDialog(
        IReadOnlyCollection<ConfiguredObjectStorageProfile>? backupProfiles = null)
        : this(backupProfiles, null, null)
    {
    }

    public AssetCollectionDialog(
        string collectionName,
        AssetCollectionType collectionType)
        : this([], collectionName, collectionType)
    {
    }

    private AssetCollectionDialog(
        IReadOnlyCollection<ConfiguredObjectStorageProfile>? backupProfiles,
        string? collectionName,
        AssetCollectionType? collectionType)
    {
        var isEditing = collectionName is not null;
        backupProfiles ??= [];
        var availableBackupProfiles = backupProfiles
            .Where(profile => profile.HasStoredSecret)
            .OrderBy(profile => GetProviderOrder(profile.Profile.Provider))
            .ThenBy(profile => profile.Profile.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Text = isEditing ? "编辑项目" : "新建项目";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = isEditing ? new Size(640, 220) : new Size(640, 410);
        MinimumSize = isEditing ? new Size(560, 210) : new Size(560, 380);
        ShowInTaskbar = false;
        Font = new Font("Segoe UI", 9F);
        AutoScaleMode = AutoScaleMode.Dpi;

        var actionRow = isEditing ? 2 : 3;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = actionRow + 1,
            Padding = new Padding(20),
            BackColor = Color.White
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        if (!isEditing)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        }

        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));

        layout.Controls.Add(CreateLabel("名称"), 0, 0);
        _nameTextBox.Dock = DockStyle.Fill;
        _nameTextBox.Margin = new Padding(0, 7, 0, 7);
        _nameTextBox.MaxLength = 120;
        _nameTextBox.AccessibleName = "项目名称";
        _nameTextBox.Text = collectionName ?? string.Empty;
        layout.Controls.Add(_nameTextBox, 1, 0);

        layout.Controls.Add(CreateLabel("类型"), 0, 1);
        _typeComboBox.Dock = DockStyle.Fill;
        _typeComboBox.Margin = new Padding(0, 7, 0, 7);
        _typeComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _typeComboBox.DisplayMember = nameof(TypeChoice.DisplayName);
        _typeComboBox.AccessibleName = "项目类型";
        _typeComboBox.Items.AddRange(CollectionTypeChoices
            .Select(choice => (object)choice)
            .ToArray());
        _typeComboBox.SelectedItem = CollectionTypeChoices.Single(choice =>
            choice.Type == (collectionType ?? AssetCollectionType.Mixed));
        layout.Controls.Add(_typeComboBox, 1, 1);

        if (!isEditing)
        {
            layout.Controls.Add(CreateLabel("云端备份"), 0, 2);
            var backupLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Margin = new Padding(0, 6, 0, 6)
            };
            backupLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            backupLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            _enableCloudBackupCheckBox.Dock = DockStyle.Fill;
            _enableCloudBackupCheckBox.Text = availableBackupProfiles.Length == 0
                ? "开启云端备份（暂无可用配置）"
                : "开启云端备份";
            _enableCloudBackupCheckBox.Enabled = availableBackupProfiles.Length > 0;
            _enableCloudBackupCheckBox.AccessibleName = "开启云端备份";
            _enableCloudBackupCheckBox.CheckedChanged += (_, _) =>
                _backupProfileListBox.Enabled = _enableCloudBackupCheckBox.Checked;
            backupLayout.Controls.Add(_enableCloudBackupCheckBox, 0, 0);

            _backupProfileListBox.Dock = DockStyle.Fill;
            _backupProfileListBox.CheckOnClick = true;
            _backupProfileListBox.Enabled = false;
            _backupProfileListBox.IntegralHeight = false;
            _backupProfileListBox.DisplayMember = nameof(BackupProfileChoice.DisplayName);
            _backupProfileListBox.AccessibleName = "云端备份配置列表";
            _backupProfileListBox.Items.AddRange(availableBackupProfiles
                .Select(profile => (object)new BackupProfileChoice(profile.Profile))
                .ToArray());
            backupLayout.Controls.Add(_backupProfileListBox, 0, 1);
            layout.Controls.Add(backupLayout, 1, 2);
        }

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 12, 0, 0)
        };
        var submitButton = new Button
        {
            Text = isEditing ? "保存" : "创建",
            Size = new Size(96, 32),
            BackColor = Color.FromArgb(24, 121, 78),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        submitButton.FlatAppearance.BorderSize = 0;
        submitButton.Click += SubmitButton_Click;
        var cancelButton = new Button
        {
            Text = "取消",
            DialogResult = DialogResult.Cancel,
            Size = new Size(88, 32),
            Margin = new Padding(8, 0, 0, 0)
        };
        buttons.Controls.Add(submitButton);
        buttons.Controls.Add(cancelButton);
        layout.Controls.Add(buttons, 0, actionRow);
        layout.SetColumnSpan(buttons, 2);

        AcceptButton = submitButton;
        CancelButton = cancelButton;
        Controls.Add(layout);
    }

    public string CollectionName => _nameTextBox.Text.Trim();

    public AssetCollectionType CollectionType =>
        ((TypeChoice)_typeComboBox.SelectedItem!).Type;

    public IReadOnlyList<Guid> BackupProfileIds => !_enableCloudBackupCheckBox.Checked
        ? []
        : _backupProfileListBox.CheckedItems
            .Cast<BackupProfileChoice>()
            .Select(choice => choice.Profile.Id)
            .ToArray();

    internal static IReadOnlyList<TypeChoice> CollectionTypeChoices { get; } =
    [
        new(AssetCollectionType.Video, "视频"),
        new(AssetCollectionType.Audio, "音频"),
        new(AssetCollectionType.Image, "图片"),
        new(AssetCollectionType.Text, "文字"),
        new(AssetCollectionType.Mixed, "综合")
    ];

    private static Label CreateLabel(string text)
    {
        return new Label
        {
            Dock = DockStyle.Fill,
            Text = text,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(52, 61, 69)
        };
    }

    private void SubmitButton_Click(object? sender, EventArgs e)
    {
        if (CollectionName.Length == 0)
        {
            MessageBox.Show(
                this,
                "请输入项目名称。",
                "CDSI Beacon",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            _nameTextBox.Focus();
            return;
        }

        if (_enableCloudBackupCheckBox.Checked && BackupProfileIds.Count == 0)
        {
            MessageBox.Show(
                this,
                "请至少勾选一个云端备份配置，或取消“开启云端备份”。",
                "CDSI Beacon",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            _backupProfileListBox.Focus();
            return;
        }

        DialogResult = DialogResult.OK;
        Close();
    }

    internal sealed record TypeChoice(
        AssetCollectionType Type,
        string DisplayName);

    private sealed record BackupProfileChoice(ObjectStorageProfile Profile)
    {
        public string DisplayName =>
            $"{FormatProvider(Profile.Provider)} · {Profile.DisplayName} · {Profile.BucketName}";

        private static string FormatProvider(ObjectStorageProvider provider)
        {
            return provider switch
            {
                ObjectStorageProvider.AliyunOss => "阿里云 OSS",
                ObjectStorageProvider.TencentCos => "腾讯云 COS",
                ObjectStorageProvider.QiniuKodo => "七牛云 Kodo",
                _ => provider.ToString()
            };
        }
    }

    private static int GetProviderOrder(ObjectStorageProvider provider)
    {
        return provider switch
        {
            ObjectStorageProvider.AliyunOss => 0,
            ObjectStorageProvider.TencentCos => 1,
            ObjectStorageProvider.QiniuKodo => 2,
            _ => int.MaxValue
        };
    }
}
