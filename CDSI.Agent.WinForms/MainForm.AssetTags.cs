using CDSI.Agent.Application.Assets;
using CDSI.Agent.Core.Assets;

namespace CDSI.Agent.WinForms;

public sealed partial class MainForm
{
    private readonly AssetTagService _assetTagService;
    private readonly ToolStripMenuItem _assetTagsMenuItem = new();
    private IReadOnlyList<AssetTagSummary> _knownAssetTags = [];

    private void ConfigureAssetTagMenu(
        ToolStripMenuItem menuItem,
        IReadOnlyList<AssetListItem> selectedAssets)
    {
        menuItem.DropDownItems.Clear();
        menuItem.Text = $"标签 ({selectedAssets.Count:N0})";
        menuItem.Enabled = selectedAssets.Count > 0 && !_isBusy;

        foreach (var presetName in AssetTagService.PresetNames)
        {
            var tag = _knownAssetTags.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, presetName, StringComparison.OrdinalIgnoreCase));
            menuItem.DropDownItems.Add(
                CreateAssetTagMenuItem(presetName, tag?.Id, selectedAssets));
        }

        var customTags = _knownAssetTags
            .Where(tag => !AssetTagService.PresetNames.Contains(
                tag.Name,
                StringComparer.OrdinalIgnoreCase))
            .OrderBy(tag => tag.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        if (customTags.Length > 0)
        {
            menuItem.DropDownItems.Add(new ToolStripSeparator());
            foreach (var tag in customTags)
            {
                menuItem.DropDownItems.Add(
                    CreateAssetTagMenuItem(tag.Name, tag.Id, selectedAssets));
            }
        }

        menuItem.DropDownItems.Add(new ToolStripSeparator());
        var customItem = new ToolStripMenuItem("自定义标签...");
        customItem.Click += async (_, _) => await AddCustomAssetTagAsync();
        menuItem.DropDownItems.Add(customItem);
    }

    private ToolStripMenuItem CreateAssetTagMenuItem(
        string tagName,
        Guid? tagId,
        IReadOnlyList<AssetListItem> selectedAssets)
    {
        var taggedCount = selectedAssets.Count(asset => asset.Tags.Contains(
            tagName,
            StringComparer.OrdinalIgnoreCase));
        var item = new ToolStripMenuItem(tagName)
        {
            CheckState = taggedCount switch
            {
                0 => CheckState.Unchecked,
                _ when taggedCount == selectedAssets.Count => CheckState.Checked,
                _ => CheckState.Indeterminate
            }
        };
        item.Click += async (_, _) => await ToggleAssetTagAsync(
            tagName,
            tagId,
            remove: taggedCount == selectedAssets.Count);
        return item;
    }

    private async Task AddCustomAssetTagAsync()
    {
        var selected = GetSelectedAssets();
        if (selected.Count == 0)
        {
            return;
        }

        using var dialog = new AssetTagDialog(_knownAssetTags, selected.Count);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        await AssignAssetTagAsync(dialog.TagName, selected);
    }

    private async Task ToggleAssetTagAsync(
        string tagName,
        Guid? tagId,
        bool remove)
    {
        var selected = GetSelectedAssets();
        if (selected.Count == 0)
        {
            return;
        }

        try
        {
            var assetIds = selected.Select(asset => asset.AssetId).ToArray();
            if (!await _stateDatabaseWriteGate.TryRunAsync(async () =>
            {
                if (remove && tagId is not null)
                {
                    var removed = await _assetTagService.RemoveAsync(tagId.Value, assetIds);
                    _statusLabel.Text = $"已从 {removed:N0} 个资产移除标签：{tagName}";
                }
                else
                {
                    var added = await _assetTagService.AssignAsync(tagName, assetIds);
                    _statusLabel.Text = added == 0
                        ? $"所选资产已有标签：{tagName}"
                        : $"已为 {added:N0} 个资产添加标签：{tagName}";
                }

                await RefreshAssetPageAsync();
            }))
            {
                return;
            }
        }
        catch (Exception exception)
        {
            ShowError(remove ? "无法移除资产标签" : "无法添加资产标签", exception);
        }
    }

    private async Task AssignAssetTagAsync(
        string tagName,
        IReadOnlyList<AssetListItem> selectedAssets)
    {
        try
        {
            if (!await _stateDatabaseWriteGate.TryRunAsync(async () =>
            {
                var added = await _assetTagService.AssignAsync(
                    tagName,
                    selectedAssets.Select(asset => asset.AssetId).ToArray());
                _statusLabel.Text = added == 0
                    ? $"所选资产已有标签：{tagName}"
                    : $"已为 {added:N0} 个资产添加标签：{tagName}";
                await RefreshAssetPageAsync();
            }))
            {
                return;
            }
        }
        catch (Exception exception)
        {
            ShowError("无法添加资产标签", exception);
        }
    }
}
