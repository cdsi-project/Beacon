using CDSI.Agent.Core.Assets;

namespace CDSI.Agent.WinForms;

public sealed partial class MainForm
{
    internal const string AllAssetExtensionsLabel = "全部扩展名";
    internal const string AllAssetTagsLabel = "全部标签";

    internal static readonly IReadOnlyList<AssetFileTypeFilterChoice>
        AssetFileTypeFilterChoices =
        [
            new(AssetFileTypeFilter.All, "全部类型"),
            new(AssetFileTypeFilter.Video, "视频"),
            new(AssetFileTypeFilter.Audio, "音频"),
            new(AssetFileTypeFilter.Image, "图片"),
            new(AssetFileTypeFilter.Document, "文档"),
            new(AssetFileTypeFilter.Other, "其他")
        ];

    private readonly ComboBox _assetFileTypeFilterComboBox = new();
    private readonly ComboBox _assetExtensionFilterComboBox = new();
    private readonly ComboBox _assetTagFilterComboBox = new();
    private readonly TextBox _assetFilenameFilterTextBox = new();
    private readonly DateTimePicker _assetCreatedFromDatePicker = new();
    private readonly DateTimePicker _assetCreatedToDatePicker = new();
    private readonly Button _applyAssetFilterButton = new();
    private readonly Button _resetAssetFilterButton = new();
    private readonly Label _assetFilterResultLabel = new();
    private readonly LinkLabel _assetDirectoryFilterLink = new();
    private AssetListFilter _assetListFilter = AssetListFilter.Empty;
    private int _assetExtensionRefreshVersion;

    private Control ConfigureAssetFilterPanel()
    {
        _assetFilenameFilterTextBox.Width = 180;
        _assetFilenameFilterTextBox.AccessibleName = "文件名搜索";
        _assetFilenameFilterTextBox.PlaceholderText = "输入文件名关键词";
        _assetFilenameFilterTextBox.KeyDown += async (_, eventArgs) =>
        {
            if (eventArgs.KeyCode != Keys.Enter)
            {
                return;
            }

            eventArgs.SuppressKeyPress = true;
            await ApplyAssetFilterAsync();
        };

        _assetFileTypeFilterComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _assetFileTypeFilterComboBox.Width = 108;
        _assetFileTypeFilterComboBox.AccessibleName = "文件类型过滤";
        _assetFileTypeFilterComboBox.Items.AddRange(
            AssetFileTypeFilterChoices.Cast<object>().ToArray());
        _assetFileTypeFilterComboBox.SelectedIndex = 0;

        _assetExtensionFilterComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _assetExtensionFilterComboBox.Width = 108;
        _assetExtensionFilterComboBox.AccessibleName = "扩展名过滤";
        _assetExtensionFilterComboBox.Items.Add(AllAssetExtensionsLabel);
        _assetExtensionFilterComboBox.SelectedIndex = 0;
        _assetFileTypeFilterComboBox.SelectionChangeCommitted += async (_, _) =>
            await RefreshAssetExtensionsForSelectedTypeAsync(
                includeUnavailableSelection: false);

        _assetTagFilterComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _assetTagFilterComboBox.Width = 128;
        _assetTagFilterComboBox.AccessibleName = "资产标签过滤";
        _assetTagFilterComboBox.Items.Add(
            new AssetTagFilterChoice(null, AllAssetTagsLabel, 0));
        _assetTagFilterComboBox.SelectedIndex = 0;

        ConfigureFilterDatePicker(
            _assetCreatedFromDatePicker,
            "创建时间开始",
            DateTime.Today.AddMonths(-1));
        ConfigureFilterDatePicker(
            _assetCreatedToDatePicker,
            "创建时间结束",
            DateTime.Today);

        ConfigureFilterButton(
            _applyAssetFilterButton,
            "搜索",
            Color.FromArgb(24, 121, 78),
            Color.White);
        ConfigureFilterButton(
            _resetAssetFilterButton,
            "重置",
            Color.FromArgb(236, 239, 242),
            Color.FromArgb(31, 37, 43));
        _applyAssetFilterButton.Click += async (_, _) =>
            await ApplyAssetFilterAsync();
        _resetAssetFilterButton.Click += async (_, _) =>
            await ResetAssetFilterAsync();

        _assetFilterResultLabel.AutoSize = true;
        _assetFilterResultLabel.Text = "全部资产";
        _assetFilterResultLabel.Margin = new Padding(10, 8, 0, 0);
        _assetFilterResultLabel.ForeColor = Color.FromArgb(88, 98, 106);
        _assetFilterResultLabel.AccessibleName = "资产过滤结果";

        _assetDirectoryFilterLink.AutoSize = true;
        _assetDirectoryFilterLink.MaximumSize = new Size(320, 0);
        _assetDirectoryFilterLink.Margin = new Padding(10, 8, 0, 0);
        _assetDirectoryFilterLink.AccessibleName = "当前目录筛选，点击取消";
        _assetDirectoryFilterLink.Visible = false;
        _assetDirectoryFilterLink.LinkClicked += async (_, _) =>
        {
            if (_isBusy || _refreshingAssetPage)
            {
                return;
            }
            var filter = _assetListFilter;
            _assetListFilter = new AssetListFilter(filter.FileType, filter.CreatedFrom, filter.CreatedBefore,
                filter.Extension, filter.TagId, filter.FilenameContains);
            _assetPageIndex = 0;
            await RefreshAssetPageAsync();
        };

        return CreateAssetFilterPanel(
            _assetFilenameFilterTextBox,
            _assetFileTypeFilterComboBox,
            _assetExtensionFilterComboBox,
            _assetTagFilterComboBox,
            _assetCreatedFromDatePicker,
            _assetCreatedToDatePicker,
            _applyAssetFilterButton,
            _resetAssetFilterButton,
            _assetFilterResultLabel,
            _assetDirectoryFilterLink);
    }

    internal static Control CreateAssetFilterPanel(
        TextBox filenameTextBox,
        ComboBox fileTypeComboBox,
        ComboBox extensionComboBox,
        ComboBox tagComboBox,
        DateTimePicker createdFromDatePicker,
        DateTimePicker createdToDatePicker,
        Button applyButton,
        Button resetButton,
        Label resultLabel,
        Control? directoryFilterControl = null)
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(0, 82),
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Margin = Padding.Empty,
            Padding = new Padding(8, 8, 8, 6),
            BackColor = Color.FromArgb(247, 248, 250)
        };
        panel.Controls.Add(CreateFilterLabel("文件名"));
        panel.Controls.Add(filenameTextBox);
        panel.Controls.Add(CreateFilterLabel("文件类型"));
        panel.Controls.Add(fileTypeComboBox);
        panel.Controls.Add(CreateFilterLabel("扩展名", leftMargin: 14));
        panel.Controls.Add(extensionComboBox);
        panel.Controls.Add(CreateFilterLabel("标签", leftMargin: 14));
        panel.Controls.Add(tagComboBox);
        panel.Controls.Add(CreateFilterLabel("创建时间", leftMargin: 14));
        panel.Controls.Add(createdFromDatePicker);
        panel.Controls.Add(CreateFilterLabel("至", leftMargin: 6));
        panel.Controls.Add(createdToDatePicker);
        panel.Controls.Add(applyButton);
        panel.Controls.Add(resetButton);
        panel.Controls.Add(resultLabel);
        if (directoryFilterControl is not null)
        {
            panel.Controls.Add(directoryFilterControl);
        }
        return panel;
    }

    internal static AssetListFilter BuildAssetListFilter(
        AssetFileTypeFilter fileType,
        bool createdFromEnabled,
        DateTime createdFrom,
        bool createdToEnabled,
        DateTime createdTo,
        string? extension = null,
        Guid? tagId = null,
        string? filenameContains = null,
        string? directoryPath = null)
    {
        if (createdFromEnabled &&
            createdToEnabled &&
            createdFrom.Date > createdTo.Date)
        {
            throw new ArgumentException("创建时间的开始日期不能晚于结束日期。");
        }

        DateTimeOffset? createdFromBoundary = createdFromEnabled
            ? StartOfLocalDay(createdFrom)
            : null;
        DateTimeOffset? createdBeforeBoundary = createdToEnabled
            ? StartOfLocalDay(createdTo.Date.AddDays(1))
            : null;
        return new AssetListFilter(
            fileType,
            createdFromBoundary,
            createdBeforeBoundary,
            extension,
            tagId,
            filenameContains,
            directoryPath);
    }

    private static DateTimeOffset StartOfLocalDay(DateTime value)
    {
        var localDate = DateTime.SpecifyKind(value.Date, DateTimeKind.Unspecified);
        var utcDate = TimeZoneInfo.ConvertTimeToUtc(localDate, TimeZoneInfo.Local);
        return new DateTimeOffset(utcDate, TimeSpan.Zero);
    }

    private static Label CreateFilterLabel(string text, int leftMargin = 0)
    {
        return new Label
        {
            AutoSize = true,
            Text = text,
            Margin = new Padding(leftMargin, 7, 6, 0),
            ForeColor = Color.FromArgb(52, 61, 69)
        };
    }

    private static void ConfigureFilterDatePicker(
        DateTimePicker datePicker,
        string accessibleName,
        DateTime value)
    {
        datePicker.Format = DateTimePickerFormat.Custom;
        datePicker.CustomFormat = "yyyy-MM-dd";
        datePicker.ShowCheckBox = true;
        datePicker.Checked = false;
        datePicker.Value = value;
        datePicker.Width = 126;
        datePicker.AccessibleName = accessibleName;
    }

    private static void ConfigureFilterButton(
        Button button,
        string text,
        Color background,
        Color foreground)
    {
        button.Text = text;
        button.Size = new Size(72, 28);
        button.Margin = new Padding(8, 0, 0, 0);
        button.BackColor = background;
        button.ForeColor = foreground;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.Cursor = Cursors.Hand;
    }

    private async Task ApplyAssetFilterAsync()
    {
        if (_assetFileTypeFilterComboBox.SelectedItem is not
            AssetFileTypeFilterChoice selectedType)
        {
            return;
        }

        try
        {
            _assetListFilter = BuildAssetListFilter(
                selectedType.Value,
                _assetCreatedFromDatePicker.Checked,
                _assetCreatedFromDatePicker.Value,
                _assetCreatedToDatePicker.Checked,
                _assetCreatedToDatePicker.Value,
                _assetExtensionFilterComboBox.SelectedIndex > 0
                    ? _assetExtensionFilterComboBox.SelectedItem as string
                    : null,
                (_assetTagFilterComboBox.SelectedItem as AssetTagFilterChoice)?.TagId,
                _assetFilenameFilterTextBox.Text,
                _assetListFilter.DirectoryPath);
        }
        catch (ArgumentException exception)
        {
            MessageBox.Show(
                this,
                exception.Message,
                "CDSI Beacon",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        _assetPageIndex = 0;
        await RefreshAssetPageAsync();
    }

    private async Task ResetAssetFilterAsync()
    {
        _assetFileTypeFilterComboBox.SelectedIndex = 0;
        _assetExtensionFilterComboBox.SelectedIndex = 0;
        _assetTagFilterComboBox.SelectedIndex = 0;
        _assetFilenameFilterTextBox.Clear();
        _assetCreatedFromDatePicker.Checked = false;
        _assetCreatedToDatePicker.Checked = false;
        _assetListFilter = AssetListFilter.Empty;
        _assetPageIndex = 0;
        await RefreshAssetExtensionsForSelectedTypeAsync(
            includeUnavailableSelection: false);
        await RefreshAssetPageAsync();
    }

    private void UpdateAssetFilterResult(long filteredCount, long totalCount)
    {
        _assetDirectoryFilterLink.Visible = _assetListFilter.DirectoryPath is not null;
        if (_assetListFilter.DirectoryPath is { } path)
        {
            var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
            _assetDirectoryFilterLink.Text = $"目录：{(name.Length == 0 ? path : name)}（取消）";
            _assetDirectoryFilterLink.AccessibleDescription = path + "，包含所有子目录";
        }
        _assetFilterResultLabel.Text = _assetListFilter.IsEmpty
            ? $"全部 {totalCount:N0}"
            : $"筛选结果 {filteredCount:N0} / {totalCount:N0}";
    }

    private void UpdateAssetFilterControlState()
    {
        var enabled = !_isBusy && !_refreshingAssetPage;
        _assetFilenameFilterTextBox.Enabled = enabled;
        _assetFileTypeFilterComboBox.Enabled = enabled;
        _assetExtensionFilterComboBox.Enabled = enabled;
        _assetTagFilterComboBox.Enabled = enabled;
        _assetCreatedFromDatePicker.Enabled = enabled;
        _assetCreatedToDatePicker.Enabled = enabled;
        _applyAssetFilterButton.Enabled = enabled;
        _resetAssetFilterButton.Enabled = enabled;
        _assetDirectoryFilterLink.Enabled = enabled;
        UpdateAssetDirectoryActionState();
    }

    internal static void RefreshAssetExtensionChoices(
        ComboBox comboBox,
        IReadOnlyList<string> extensions,
        bool includeUnavailableSelection = true)
    {
        var selectedExtension = comboBox.SelectedIndex > 0
            ? comboBox.SelectedItem as string
            : null;
        var choices = extensions
            .Where(extension => !string.IsNullOrWhiteSpace(extension))
            .Select(extension => extension.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (selectedExtension is not null &&
            includeUnavailableSelection &&
            !choices.Contains(selectedExtension, StringComparer.OrdinalIgnoreCase))
        {
            choices.Add(selectedExtension);
            choices.Sort(StringComparer.OrdinalIgnoreCase);
        }

        comboBox.BeginUpdate();
        try
        {
            comboBox.Items.Clear();
            comboBox.Items.Add(AllAssetExtensionsLabel);
            comboBox.Items.AddRange(choices.Cast<object>().ToArray());
            comboBox.SelectedItem = selectedExtension ?? AllAssetExtensionsLabel;
            if (comboBox.SelectedIndex < 0)
            {
                comboBox.SelectedIndex = 0;
            }
        }
        finally
        {
            comboBox.EndUpdate();
        }
    }

    internal static void RefreshAssetTagChoices(
        ComboBox comboBox,
        IReadOnlyList<AssetTagSummary> tags,
        Guid? selectedTagId = null)
    {
        selectedTagId ??= (comboBox.SelectedItem as AssetTagFilterChoice)?.TagId;
        var choices = tags
            .OrderBy(tag => tag.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(tag => new AssetTagFilterChoice(tag.Id, tag.Name, tag.AssetCount))
            .ToArray();

        comboBox.BeginUpdate();
        try
        {
            comboBox.Items.Clear();
            comboBox.Items.Add(new AssetTagFilterChoice(null, AllAssetTagsLabel, 0));
            comboBox.Items.AddRange(choices.Cast<object>().ToArray());
            comboBox.SelectedItem = comboBox.Items
                .Cast<AssetTagFilterChoice>()
                .FirstOrDefault(choice => choice.TagId == selectedTagId)
                ?? comboBox.Items[0];
        }
        finally
        {
            comboBox.EndUpdate();
        }
    }

    private async Task RefreshAssetExtensionsForSelectedTypeAsync(
        bool includeUnavailableSelection)
    {
        if (_assetFileTypeFilterComboBox.SelectedItem is not
            AssetFileTypeFilterChoice selectedType)
        {
            return;
        }

        var refreshVersion = ++_assetExtensionRefreshVersion;
        try
        {
            var extensions = await _scanService.ListAssetExtensionsAsync(
                selectedType.Value);
            if (refreshVersion != _assetExtensionRefreshVersion ||
                IsDisposed ||
                _assetFileTypeFilterComboBox.SelectedItem is not
                    AssetFileTypeFilterChoice currentType ||
                currentType.Value != selectedType.Value)
            {
                return;
            }

            RefreshAssetExtensionChoices(
                _assetExtensionFilterComboBox,
                extensions,
                includeUnavailableSelection);
        }
        catch (Exception exception)
            when (exception is not OperationCanceledException)
        {
            if (refreshVersion == _assetExtensionRefreshVersion && !IsDisposed)
            {
                _statusLabel.Text = $"扩展名加载失败：{exception.Message}";
            }
        }
    }

    internal sealed record AssetFileTypeFilterChoice(
        AssetFileTypeFilter Value,
        string Label)
    {
        public override string ToString()
        {
            return Label;
        }
    }

    internal sealed record AssetTagFilterChoice(
        Guid? TagId,
        string Name,
        int AssetCount)
    {
        public override string ToString()
        {
            return TagId is null ? Name : $"{Name} ({AssetCount:N0})";
        }
    }
}
