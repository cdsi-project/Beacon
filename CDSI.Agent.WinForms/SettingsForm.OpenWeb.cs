using System.Diagnostics;
using CDSI.Agent.Application.OpenWeb;
using CDSI.Agent.Core.OpenWeb;

namespace CDSI.Agent.WinForms;

public sealed partial class SettingsForm
{
    private readonly OpenWebSettingsService _openWebSettingsService;
    private readonly DataGridView _openWebSourcesGrid = new();
    private readonly ContextMenuStrip _openWebSourceContextMenu = new();
    private readonly ToolStripMenuItem _editOpenWebSourceMenuItem = new();
    private readonly ToolStripMenuItem _openOpenWebSourceMenuItem = new();
    private readonly ToolStripMenuItem _copyOpenWebDomainMenuItem = new();
    private readonly ToolStripMenuItem _defaultOpenWebSourceMenuItem = new();
    private readonly ToolStripMenuItem _deleteOpenWebSourceMenuItem = new();

    private TabPage CreateOpenWebPage()
    {
        var page = new TabPage("OpenWeb")
        {
            BackColor = Color.White,
            Padding = new Padding(16)
        };
        ConfigureOpenWebSourcesGrid();

        var addButton = CreateButton(
            "添加源站",
            Color.FromArgb(24, 121, 78),
            Color.White);
        addButton.Size = new Size(104, 32);
        addButton.AccessibleName = "添加 OpenWeb 源站";
        addButton.Click += AddOpenWebSourceButton_Click;

        var commands = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 46,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(0, 4, 0, 8)
        };
        commands.Controls.Add(addButton);

        page.Controls.Add(_openWebSourcesGrid);
        page.Controls.Add(commands);
        return page;
    }

    private void ConfigureOpenWebSourcesGrid()
    {
        _openWebSourcesGrid.Dock = DockStyle.Fill;
        _openWebSourcesGrid.BackgroundColor = Color.White;
        _openWebSourcesGrid.BorderStyle = BorderStyle.FixedSingle;
        _openWebSourcesGrid.ReadOnly = true;
        _openWebSourcesGrid.AllowUserToAddRows = false;
        _openWebSourcesGrid.AllowUserToDeleteRows = false;
        _openWebSourcesGrid.AllowUserToResizeRows = false;
        _openWebSourcesGrid.AutoGenerateColumns = false;
        _openWebSourcesGrid.MultiSelect = false;
        _openWebSourcesGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _openWebSourcesGrid.RowHeadersVisible = false;
        _openWebSourcesGrid.RowTemplate.Height = 30;
        _openWebSourcesGrid.ColumnHeadersHeight = 36;
        _openWebSourcesGrid.AccessibleName = "OpenWeb 源站列表";
        _openWebSourcesGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "名称",
            Width = 150
        });
        _openWebSourcesGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "源站域名",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            MinimumWidth = 220,
            FillWeight = 100
        });
        _openWebSourcesGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "WordPress 用户名",
            Width = 160
        });
        _openWebSourcesGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "默认",
            Width = 72
        });
        _openWebSourcesGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "凭据",
            Width = 82
        });
        _editOpenWebSourceMenuItem.Text = "编辑源站";
        _editOpenWebSourceMenuItem.Click += EditOpenWebSourceMenuItem_Click;
        _openOpenWebSourceMenuItem.Text = "打开源站";
        _openOpenWebSourceMenuItem.Click += (_, _) => OpenSelectedOpenWebSource();
        _copyOpenWebDomainMenuItem.Text = "复制源站域名";
        _copyOpenWebDomainMenuItem.Click += (_, _) => CopySelectedOpenWebDomain();
        _defaultOpenWebSourceMenuItem.Text = "设为默认";
        _defaultOpenWebSourceMenuItem.Click += DefaultOpenWebSourceMenuItem_Click;
        _deleteOpenWebSourceMenuItem.Text = "删除源站";
        _deleteOpenWebSourceMenuItem.ForeColor = Color.FromArgb(137, 49, 49);
        _deleteOpenWebSourceMenuItem.Click += DeleteOpenWebSourceMenuItem_Click;
        _openWebSourceContextMenu.Items.AddRange(
        [
            _editOpenWebSourceMenuItem,
            _openOpenWebSourceMenuItem,
            _copyOpenWebDomainMenuItem,
            new ToolStripSeparator(),
            _defaultOpenWebSourceMenuItem,
            new ToolStripSeparator(),
            _deleteOpenWebSourceMenuItem
        ]);
        _openWebSourceContextMenu.Opening += (_, args) =>
        {
            var source =
                (_openWebSourcesGrid.CurrentRow?.Tag as ConfiguredOpenWebSource)?.Source;
            args.Cancel = source is null;
            _defaultOpenWebSourceMenuItem.Enabled = source is not null && !source.IsDefault;
        };
        _openWebSourcesGrid.ContextMenuStrip = _openWebSourceContextMenu;
        _openWebSourcesGrid.CellMouseDown += (_, args) =>
        {
            if (args.Button == MouseButtons.Right &&
                args.RowIndex >= 0 &&
                args.ColumnIndex >= 0)
            {
                _openWebSourcesGrid.CurrentCell =
                    _openWebSourcesGrid.Rows[args.RowIndex].Cells[args.ColumnIndex];
            }
        };
        _openWebSourcesGrid.CellDoubleClick += OpenWebSourcesGrid_CellDoubleClick;
    }

    private async Task RefreshOpenWebAsync()
    {
        var sources = await _openWebSettingsService.ListAsync();
        _openWebSourcesGrid.Rows.Clear();
        foreach (var configured in sources)
        {
            var source = configured.Source;
            var index = _openWebSourcesGrid.Rows.Add(
                source.DisplayName,
                source.OriginDomain,
                source.WordPressUsername,
                source.IsDefault ? "是" : string.Empty,
                configured.HasApplicationPassword ? "已保存" : "缺失");
            _openWebSourcesGrid.Rows[index].Tag = configured;
        }
    }

    private async void AddOpenWebSourceButton_Click(object? sender, EventArgs e)
    {
        await ShowOpenWebSourceDialogAsync(null);
    }

    private async void EditOpenWebSourceMenuItem_Click(object? sender, EventArgs e)
    {
        await EditSelectedOpenWebSourceAsync();
    }

    private async void OpenWebSourcesGrid_CellDoubleClick(
        object? sender,
        DataGridViewCellEventArgs e)
    {
        if (e.RowIndex >= 0)
        {
            await EditSelectedOpenWebSourceAsync();
        }
    }

    private async Task EditSelectedOpenWebSourceAsync()
    {
        if (_openWebSourcesGrid.CurrentRow?.Tag is ConfiguredOpenWebSource configured)
        {
            await ShowOpenWebSourceDialogAsync(configured.Source);
        }
    }

    private async Task ShowOpenWebSourceDialogAsync(OpenWebSource? source)
    {
        using var dialog = new OpenWebSourceDialog(source);
        while (dialog.ShowDialog(this) == DialogResult.OK)
        {
            try
            {
                if (!await TryRunStateDatabaseWriteAsync(async () =>
                {
                    await _openWebSettingsService.SaveAsync(dialog.CreateRequest());
                    await RefreshOpenWebAsync();
                }))
                {
                    return;
                }

                return;
            }
            catch (Exception exception)
            {
                ShowError("无法保存 OpenWeb 源站", exception);
                dialog.DialogResult = DialogResult.None;
            }
        }
    }

    private void OpenSelectedOpenWebSource()
    {
        if (_openWebSourcesGrid.CurrentRow?.Tag is not ConfiguredOpenWebSource configured)
        {
            return;
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = $"https://{configured.Source.OriginDomain}",
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            ShowError("无法打开 OpenWeb 源站", exception);
        }
    }

    private void CopySelectedOpenWebDomain()
    {
        if (_openWebSourcesGrid.CurrentRow?.Tag is not ConfiguredOpenWebSource configured)
        {
            return;
        }

        try
        {
            Clipboard.SetText(configured.Source.OriginDomain);
        }
        catch (Exception exception)
        {
            ShowError("无法复制 OpenWeb 源站域名", exception);
        }
    }

    private async void DefaultOpenWebSourceMenuItem_Click(object? sender, EventArgs e)
    {
        if (_openWebSourcesGrid.CurrentRow?.Tag is not ConfiguredOpenWebSource configured ||
            configured.Source.IsDefault)
        {
            return;
        }

        try
        {
            if (!await TryRunStateDatabaseWriteAsync(async () =>
            {
                await _openWebSettingsService.SetDefaultAsync(configured.Source.Id);
                await RefreshOpenWebAsync();
            }))
            {
                return;
            }
        }
        catch (Exception exception)
        {
            ShowError("无法设置默认 OpenWeb 源站", exception);
        }
    }

    private async void DeleteOpenWebSourceMenuItem_Click(object? sender, EventArgs e)
    {
        if (_openWebSourcesGrid.CurrentRow?.Tag is not ConfiguredOpenWebSource configured ||
            MessageBox.Show(
                this,
                "将删除本机源站配置和对应的 Windows 凭据，不会删除 WordPress 中的文章。",
                "删除 OpenWeb 源站",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Warning) != DialogResult.OK)
        {
            return;
        }

        try
        {
            if (!await TryRunStateDatabaseWriteAsync(async () =>
            {
                await _openWebSettingsService.DeleteAsync(configured.Source.Id);
                await RefreshOpenWebAsync();
            }))
            {
                return;
            }
        }
        catch (Exception exception)
        {
            ShowError("无法删除 OpenWeb 源站", exception);
        }
    }
}
