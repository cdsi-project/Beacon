using System.Diagnostics;
using CDSI.Agent.Application.Git;
using CDSI.Agent.Core.Git;

namespace CDSI.Agent.WinForms;

public sealed partial class SettingsForm
{
    private readonly GitProfileService _gitProfileService;
    private readonly DataGridView _gitProfilesGrid = new();
    private readonly ContextMenuStrip _gitProfileContextMenu = new();
    private readonly ToolStripMenuItem _editGitProfileMenuItem = new();
    private readonly ToolStripMenuItem _openGitProviderMenuItem = new();
    private readonly ToolStripMenuItem _copyGitRepositoryUrlMenuItem = new();
    private readonly ToolStripMenuItem _defaultGitProfileMenuItem = new();
    private readonly ToolStripMenuItem _deleteGitProfileMenuItem = new();

    private TabPage CreateGitPage()
    {
        var page = new TabPage("Git 配置")
        {
            BackColor = Color.White,
            Padding = new Padding(16)
        };
        ConfigureGitProfilesGrid();

        var addButton = CreateButton(
            "添加配置",
            Color.FromArgb(24, 121, 78),
            Color.White);
        addButton.Size = new Size(104, 32);
        addButton.AccessibleName = "添加 Git 配置";
        addButton.Click += AddGitProfileButton_Click;

        var commands = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 46,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(0, 4, 0, 8)
        };
        commands.Controls.Add(addButton);

        var note = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 30,
            Text = "保存配置不会自动克隆、提交、推送或修改仓库中的文件。",
            ForeColor = Color.FromArgb(88, 98, 106),
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(4, 0, 0, 0)
        };

        page.Controls.Add(_gitProfilesGrid);
        page.Controls.Add(note);
        page.Controls.Add(commands);
        return page;
    }

    private void ConfigureGitProfilesGrid()
    {
        _gitProfilesGrid.Dock = DockStyle.Fill;
        _gitProfilesGrid.BackgroundColor = Color.White;
        _gitProfilesGrid.BorderStyle = BorderStyle.FixedSingle;
        _gitProfilesGrid.ReadOnly = true;
        _gitProfilesGrid.AllowUserToAddRows = false;
        _gitProfilesGrid.AllowUserToDeleteRows = false;
        _gitProfilesGrid.AllowUserToResizeRows = false;
        _gitProfilesGrid.AllowUserToResizeColumns = true;
        _gitProfilesGrid.AutoGenerateColumns = false;
        _gitProfilesGrid.MultiSelect = false;
        _gitProfilesGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _gitProfilesGrid.RowHeadersVisible = false;
        _gitProfilesGrid.RowTemplate.Height = 30;
        _gitProfilesGrid.ColumnHeadersHeight = 36;
        _gitProfilesGrid.ScrollBars = ScrollBars.Both;
        _gitProfilesGrid.AccessibleName = "Git 配置列表";
        _gitProfilesGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "名称",
            Width = 130
        });
        _gitProfilesGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "平台",
            Width = 92
        });
        _gitProfilesGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "仓库地址",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            MinimumWidth = 240,
            FillWeight = 100
        });
        _gitProfilesGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "默认分支",
            Width = 100
        });
        _gitProfilesGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "访问方式",
            Width = 88
        });
        _gitProfilesGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "用户名 / SSH 公钥",
            Width = 120
        });
        _gitProfilesGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "默认",
            Width = 62
        });
        _gitProfilesGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "凭据",
            Width = 82
        });
        _editGitProfileMenuItem.Text = "编辑配置";
        _editGitProfileMenuItem.Click += EditGitProfileMenuItem_Click;
        _openGitProviderMenuItem.Text = "打开平台网站";
        _openGitProviderMenuItem.Click += (_, _) => OpenSelectedGitProvider();
        _copyGitRepositoryUrlMenuItem.Text = "复制仓库地址";
        _copyGitRepositoryUrlMenuItem.Click += (_, _) =>
            CopySelectedGitRepositoryUrl();
        _defaultGitProfileMenuItem.Text = "设为默认";
        _defaultGitProfileMenuItem.Click += DefaultGitProfileMenuItem_Click;
        _deleteGitProfileMenuItem.Text = "删除配置";
        _deleteGitProfileMenuItem.ForeColor = Color.FromArgb(137, 49, 49);
        _deleteGitProfileMenuItem.Click += DeleteGitProfileMenuItem_Click;
        _gitProfileContextMenu.Items.AddRange(
        [
            _editGitProfileMenuItem,
            _openGitProviderMenuItem,
            _copyGitRepositoryUrlMenuItem,
            new ToolStripSeparator(),
            _defaultGitProfileMenuItem,
            new ToolStripSeparator(),
            _deleteGitProfileMenuItem
        ]);
        _gitProfileContextMenu.Opening += (_, args) =>
        {
            var profile =
                (_gitProfilesGrid.CurrentRow?.Tag as ConfiguredGitProfile)?.Profile;
            args.Cancel = profile is null;
            _defaultGitProfileMenuItem.Enabled = profile is not null && !profile.IsDefault;
        };
        _gitProfilesGrid.ContextMenuStrip = _gitProfileContextMenu;
        _gitProfilesGrid.CellMouseDown += (_, args) =>
        {
            if (args.Button == MouseButtons.Right &&
                args.RowIndex >= 0 &&
                args.ColumnIndex >= 0)
            {
                _gitProfilesGrid.CurrentCell =
                    _gitProfilesGrid.Rows[args.RowIndex].Cells[args.ColumnIndex];
            }
        };
        _gitProfilesGrid.CellDoubleClick += GitProfilesGrid_CellDoubleClick;
    }

    private async Task RefreshGitProfilesAsync()
    {
        var profiles = await _gitProfileService.ListAsync();
        _gitProfilesGrid.Rows.Clear();
        foreach (var configured in profiles)
        {
            var profile = configured.Profile;
            var index = _gitProfilesGrid.Rows.Add(
                profile.DisplayName,
                FormatGitProvider(profile.Provider),
                profile.RepositoryUrl,
                profile.DefaultBranch,
                GitProfileDialog.GetAuthenticationDisplayName(
                    profile.AuthenticationMethod),
                profile.AuthenticationMethod == GitAuthenticationMethod.Password
                    ? profile.Username
                    : Path.GetFileName(profile.SshPublicKeyPath) ?? string.Empty,
                profile.IsDefault ? "是" : string.Empty,
                profile.AuthenticationMethod == GitAuthenticationMethod.Password
                    ? configured.HasPassword ? "已保存" : "缺失"
                    : "本机密钥");
            _gitProfilesGrid.Rows[index].Tag = configured;
        }
    }

    private async void AddGitProfileButton_Click(object? sender, EventArgs e)
    {
        await ShowGitProfileDialogAsync(null);
    }

    private async void EditGitProfileMenuItem_Click(object? sender, EventArgs e)
    {
        await EditSelectedGitProfileAsync();
    }

    private async void GitProfilesGrid_CellDoubleClick(
        object? sender,
        DataGridViewCellEventArgs e)
    {
        if (e.RowIndex >= 0)
        {
            await EditSelectedGitProfileAsync();
        }
    }

    private async Task EditSelectedGitProfileAsync()
    {
        if (_gitProfilesGrid.CurrentRow?.Tag is ConfiguredGitProfile configured)
        {
            await ShowGitProfileDialogAsync(configured.Profile);
        }
    }

    private async Task ShowGitProfileDialogAsync(GitProfile? profile)
    {
        using var dialog = new GitProfileDialog(profile);
        while (dialog.ShowDialog(this) == DialogResult.OK)
        {
            try
            {
                if (!await TryRunStateDatabaseWriteAsync(async () =>
                {
                    await _gitProfileService.SaveAsync(dialog.CreateRequest());
                    await RefreshGitProfilesAsync();
                }))
                {
                    return;
                }

                return;
            }
            catch (Exception exception)
            {
                ShowError("无法保存 Git 配置", exception);
                dialog.DialogResult = DialogResult.None;
            }
        }
    }

    private void OpenSelectedGitProvider()
    {
        if (_gitProfilesGrid.CurrentRow?.Tag is not ConfiguredGitProfile configured)
        {
            return;
        }

        try
        {
            using var process = Process.Start(
                SshKeySupport.CreateOpenWebsiteStartInfo(configured.Profile.Provider));
        }
        catch (Exception exception)
        {
            ShowError("无法打开 Git 托管平台网站", exception);
        }
    }

    private void CopySelectedGitRepositoryUrl()
    {
        if (_gitProfilesGrid.CurrentRow?.Tag is not ConfiguredGitProfile configured)
        {
            return;
        }

        try
        {
            Clipboard.SetText(configured.Profile.RepositoryUrl);
        }
        catch (Exception exception)
        {
            ShowError("无法复制 Git 仓库地址", exception);
        }
    }

    private async void DefaultGitProfileMenuItem_Click(object? sender, EventArgs e)
    {
        if (_gitProfilesGrid.CurrentRow?.Tag is not ConfiguredGitProfile configured ||
            configured.Profile.IsDefault)
        {
            return;
        }

        try
        {
            if (!await TryRunStateDatabaseWriteAsync(async () =>
            {
                await _gitProfileService.SetDefaultAsync(configured.Profile.Id);
                await RefreshGitProfilesAsync();
            }))
            {
                return;
            }
        }
        catch (Exception exception)
        {
            ShowError("无法设置默认 Git 配置", exception);
        }
    }

    private async void DeleteGitProfileMenuItem_Click(object? sender, EventArgs e)
    {
        if (_gitProfilesGrid.CurrentRow?.Tag is not ConfiguredGitProfile configured ||
            MessageBox.Show(
                this,
                "将删除本机 Git 配置和对应的 Windows 凭据，不会删除远端仓库、本地文件或 SSH 密钥。",
                "删除 Git 配置",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Warning) != DialogResult.OK)
        {
            return;
        }

        try
        {
            if (!await TryRunStateDatabaseWriteAsync(async () =>
            {
                await _gitProfileService.DeleteAsync(configured.Profile.Id);
                await RefreshGitProfilesAsync();
            }))
            {
                return;
            }
        }
        catch (Exception exception)
        {
            ShowError("无法删除 Git 配置", exception);
        }
    }

    internal static string FormatGitProvider(GitHostingProvider provider)
    {
        return provider switch
        {
            GitHostingProvider.GitHub => "GitHub",
            GitHostingProvider.Gitee => "Gitee（码云）",
            _ => provider.ToString()
        };
    }
}
