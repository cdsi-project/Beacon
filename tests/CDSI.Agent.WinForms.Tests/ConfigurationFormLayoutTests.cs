using CDSI.Agent.Application.Git;
using CDSI.Agent.Application.OpenWeb;
using CDSI.Agent.Application.Scanning;
using CDSI.Agent.Application.Storage;
using CDSI.Agent.Application.Workspaces;
using CDSI.Agent.Infrastructure.FileSystem;
using CDSI.Agent.Infrastructure.Persistence;
using CDSI.Agent.Infrastructure.Security;
using CDSI.Agent.WinForms;
using CDSI.Agent.Core.Assets;
using CDSI.Agent.Core.Scanning;
using CDSI.Agent.Core.Storage;

namespace CDSI.Agent.WinForms.Tests;

public sealed class ConfigurationFormLayoutTests
{
    [Fact]
    public void FirstRunSetupForm_ProvidesAStableWorkspacePathControl()
    {
        using var form = new FirstRunSetupForm();
        form.CreateControl();

        var pathTextBox = Descendants(form)
            .OfType<TextBox>()
            .Single(control => control.AccessibleName == "工作目录路径");

        Assert.False(string.IsNullOrWhiteSpace(form.SelectedPath));
        Assert.Equal(DockStyle.Fill, pathTextBox.Dock);
        Assert.True(form.ClientSize.Width >= 560);
    }

    [Fact]
    public void SettingsForm_SeparatesWorkspaceAndExternalScanRoots()
    {
        var repository = new SqliteAssetRepository(
            Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db"));
        using var form = new SettingsForm(
            new WorkspaceApplicationService(
                repository,
                new WorkspaceProvisioner()),
            new ScanRootManagementService(repository),
            new ObjectStorageProfileService(
                repository,
                new WindowsCredentialSecretStore()),
            new OpenWebSettingsService(
                repository,
                new WindowsCredentialSecretStore()),
            new GitProfileService(
                repository,
                new WindowsCredentialSecretStore()),
            new StateDatabaseWriteGate());
        form.CreateControl();

        var tabs = Assert.Single(Descendants(form).OfType<TabControl>());
        var rootsGrid = Descendants(form)
            .OfType<DataGridView>()
            .Single(grid => grid.AccessibleName == "外部扫描目录列表");

        var storageGrid = Descendants(form)
            .OfType<DataGridView>()
            .Single(grid => grid.AccessibleName == "备份配置列表");

        var openWebSourcesGrid = Descendants(form)
            .OfType<DataGridView>()
            .Single(grid => grid.AccessibleName == "OpenWeb 源站列表");
        var gitProfilesGrid = Descendants(form)
            .OfType<DataGridView>()
            .Single(grid => grid.AccessibleName == "Git 配置列表");
        var startScanButton = Descendants(form)
            .OfType<Button>()
            .Single(button => button.Text == "开始扫描");
        var closeButton = Descendants(form)
            .OfType<Button>()
            .Single(button => button.Text == "关闭");

        Assert.Equal(5, tabs.TabPages.Count);
        Assert.Equal("工作目录", tabs.TabPages[0].Text);
        Assert.Equal("扫描目录", tabs.TabPages[1].Text);
        Assert.Equal("备份配置", tabs.TabPages[2].Text);
        Assert.Equal("OpenWeb", tabs.TabPages[3].Text);
        Assert.Equal("Git 配置", tabs.TabPages[4].Text);
        Assert.Equal(5, openWebSourcesGrid.Columns.Count);
        Assert.Equal(
            ["名称", "源站域名", "WordPress 用户名", "默认", "凭据"],
            openWebSourcesGrid.Columns
                .Cast<DataGridViewColumn>()
                .Select(column => column.HeaderText)
                .ToArray());
        Assert.Equal(
            ["添加源站"],
            Descendants(tabs.TabPages[3])
                .OfType<Button>()
                .Select(button => button.Text)
                .ToArray());
        var openWebContextMenu = Assert.IsType<ContextMenuStrip>(
            openWebSourcesGrid.ContextMenuStrip);
        Assert.Collection(
            openWebContextMenu.Items.Cast<ToolStripItem>(),
            item => Assert.Equal("编辑源站", item.Text),
            item => Assert.Equal("打开源站", item.Text),
            item => Assert.Equal("复制源站域名", item.Text),
            item => Assert.IsType<ToolStripSeparator>(item),
            item => Assert.Equal("设为默认", item.Text),
            item => Assert.IsType<ToolStripSeparator>(item),
            item => Assert.Equal("删除源站", item.Text));
        Assert.Equal(5, rootsGrid.Columns.Count);
        Assert.Equal(
            ["目录", "扫描策略", "空闲扫描", "状态", "最近扫描"],
            rootsGrid.Columns
                .Cast<DataGridViewColumn>()
                .Select(column => column.HeaderText)
                .ToArray());
        Assert.DoesNotContain(
            Descendants(form).OfType<Button>(),
            button => button.Text == "设置类型");
        Assert.DoesNotContain(
            Descendants(form).OfType<Button>(),
            button => button.Text is "停用" or "移除");
        var rootContextMenu = Assert.IsType<ContextMenuStrip>(
            rootsGrid.ContextMenuStrip);
        Assert.Collection(
            rootContextMenu.Items.Cast<ToolStripItem>(),
            item => Assert.Equal("编辑扫描设置", item.Text),
            item => Assert.Equal("停用", item.Text),
            item => Assert.IsType<ToolStripSeparator>(item),
            item => Assert.Equal("移除", item.Text));
        Assert.Equal(DataGridViewAutoSizeColumnMode.Fill, rootsGrid.Columns[0].AutoSizeMode);
        Assert.True(rootsGrid.Columns[0].MinimumWidth >= 280);
        Assert.Equal(6, storageGrid.Columns.Count);
        Assert.Equal(
            ["提供商", "名称", "Endpoint", "Bucket", "地域", "凭据"],
            storageGrid.Columns
                .Cast<DataGridViewColumn>()
                .Select(column => column.HeaderText)
                .ToArray());
        Assert.Equal(
            ["添加配置"],
            Descendants(tabs.TabPages[2])
                .OfType<Button>()
                .Select(button => button.Text)
                .ToArray());
        var storageContextMenu = Assert.IsType<ContextMenuStrip>(
            storageGrid.ContextMenuStrip);
        Assert.Collection(
            storageContextMenu.Items.Cast<ToolStripItem>(),
            item => Assert.Equal("编辑配置", item.Text),
            item => Assert.IsType<ToolStripSeparator>(item),
            item => Assert.Equal("复制 Endpoint", item.Text),
            item => Assert.Equal("复制 Bucket 名称", item.Text),
            item => Assert.IsType<ToolStripSeparator>(item),
            item => Assert.Equal("删除配置", item.Text));
        Assert.Equal(
            [
                "名称", "平台", "仓库地址", "默认分支", "访问方式",
                "用户名 / SSH 公钥", "默认", "凭据"
            ],
            gitProfilesGrid.Columns
                .Cast<DataGridViewColumn>()
                .Select(column => column.HeaderText)
                .ToArray());
        Assert.Equal(
            ["添加配置"],
            Descendants(tabs.TabPages[4])
                .OfType<Button>()
                .Select(button => button.Text)
                .ToArray());
        var gitContextMenu = Assert.IsType<ContextMenuStrip>(
            gitProfilesGrid.ContextMenuStrip);
        Assert.Collection(
            gitContextMenu.Items.Cast<ToolStripItem>(),
            item => Assert.Equal("编辑配置", item.Text),
            item => Assert.Equal("打开平台网站", item.Text),
            item => Assert.Equal("复制仓库地址", item.Text),
            item => Assert.IsType<ToolStripSeparator>(item),
            item => Assert.Equal("设为默认", item.Text),
            item => Assert.IsType<ToolStripSeparator>(item),
            item => Assert.Equal("删除配置", item.Text));
        Assert.False(startScanButton.Enabled);
        Assert.Equal(DialogResult.OK, startScanButton.DialogResult);
        Assert.Equal(DialogResult.Cancel, closeButton.DialogResult);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void SettingsForm_CloseGuard_ProtectsOnlyAnActiveAsyncOperation(
        bool asyncOperationInProgress,
        bool expected)
    {
        Assert.Equal(
            expected,
            SettingsForm.ShouldCancelClose(asyncOperationInProgress));
    }

    [Fact]
    public async Task SettingsForm_StateWriteOperation_HoldsTheSharedGateUntilCompletion()
    {
        var gate = new StateDatabaseWriteGate();
        using var form = CreateSettingsForm(gate);
        var operationStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseOperation = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var operation = form.TryRunStateDatabaseWriteAsync(async () =>
        {
            operationStarted.SetResult();
            await releaseOperation.Task;
        });
        await operationStarted.Task;

        var suspensionTask = gate.SuspendAsync();

        Assert.True(form.AsyncOperationInProgress);
        Assert.True(SettingsForm.ShouldCancelClose(form.AsyncOperationInProgress));
        Assert.False(suspensionTask.IsCompleted);

        releaseOperation.SetResult();
        Assert.True(await operation);
        Assert.False(form.AsyncOperationInProgress);
        using var suspension = await suspensionTask;
    }

    [Fact]
    public async Task SettingsForm_StateWriteOperation_RecoversBusyStateWhenRejectedOrFailed()
    {
        var gate = new StateDatabaseWriteGate();
        using var form = CreateSettingsForm(gate);
        using (await gate.SuspendAsync())
        {
            var invoked = false;
            Assert.False(await form.TryRunStateDatabaseWriteAsync(() =>
            {
                invoked = true;
                return Task.CompletedTask;
            }));
            Assert.False(invoked);
            Assert.False(form.AsyncOperationInProgress);
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            form.TryRunStateDatabaseWriteAsync(() =>
                throw new InvalidOperationException("write failed")));
        Assert.False(form.AsyncOperationInProgress);
    }

    [Fact]
    public void ScanRootDialog_DefaultsToAllFileTypesWithoutOverlappingControls()
    {
        using var dialog = new ScanRootDialog(Path.GetTempPath());
        dialog.CreateControl();
        dialog.PerformLayout();
        var strategyCheckBoxes = Descendants(dialog)
            .OfType<CheckBox>()
            .Where(control => control.AccessibleName?.StartsWith(
                "扫描策略 ",
                StringComparison.Ordinal) == true)
            .ToArray();
        var fileTypeLabel = Descendants(dialog)
            .OfType<Label>()
            .Single(label => label.Text == "扫描策略");
        var videoCheckBox = Assert.Single(
            strategyCheckBoxes,
            control => control.AccessibleName == "扫描策略 视频");
        var whitelistInput = Descendants(dialog)
            .OfType<TextBox>()
            .Single(control => control.AccessibleName == "白名单扩展名输入");
        var idleScanCheckBox = Descendants(dialog)
            .OfType<CheckBox>()
            .Single(control => control.AccessibleName == "空闲时扫描");
        var idleScanIntervalInput = Descendants(dialog)
            .OfType<NumericUpDown>()
            .Single(control => control.AccessibleName == "空闲扫描间隔");
        var idleScanUnitComboBox = Descendants(dialog)
            .OfType<ComboBox>()
            .Single(control => control.AccessibleName == "空闲扫描时间单位");

        Assert.Equal(6, strategyCheckBoxes.Length);
        Assert.Equal(ScanFileFilter.AllFileTypes, dialog.FileTypeFilters);
        Assert.All(strategyCheckBoxes.Take(5), checkBox => Assert.True(checkBox.Checked));
        Assert.False(dialog.IsWhitelistSelected);
        Assert.False(whitelistInput.Enabled);
        Assert.False(idleScanCheckBox.Checked);
        Assert.False(idleScanIntervalInput.Enabled);
        Assert.False(idleScanUnitComboBox.Enabled);
        Assert.Equal(IdleScanSchedule.Disabled, dialog.IdleScanSchedule);
        Assert.False(fileTypeLabel.Bounds.IntersectsWith(videoCheckBox.Bounds));
    }

    [Fact]
    public void ScanRootDialog_AllowsAnIdleScanIntervalInDays()
    {
        using var dialog = new ScanRootDialog(
            Path.GetTempPath(),
            idleScanSchedule: new IdleScanSchedule(
                true,
                3,
                IdleScanIntervalUnit.Days));
        dialog.CreateControl();
        var intervalInput = Descendants(dialog)
            .OfType<NumericUpDown>()
            .Single(control => control.AccessibleName == "空闲扫描间隔");
        var unitComboBox = Descendants(dialog)
            .OfType<ComboBox>()
            .Single(control => control.AccessibleName == "空闲扫描时间单位");

        Assert.True(intervalInput.Enabled);
        Assert.Equal(3, intervalInput.Value);
        Assert.True(unitComboBox.Enabled);
        Assert.Equal("天", unitComboBox.Text);
        Assert.Equal(
            new IdleScanSchedule(true, 3, IdleScanIntervalUnit.Days),
            dialog.IdleScanSchedule);
    }

    [Theory]
    [InlineData(false, 1, IdleScanIntervalUnit.Hours, "关闭")]
    [InlineData(true, 15, IdleScanIntervalUnit.Minutes, "每 15 分钟")]
    [InlineData(true, 2, IdleScanIntervalUnit.Hours, "每 2 小时")]
    [InlineData(true, 7, IdleScanIntervalUnit.Days, "每 7 天")]
    public void FormatIdleScanSchedule_UsesAReadableSummary(
        bool enabled,
        int interval,
        IdleScanIntervalUnit unit,
        string expected)
    {
        Assert.Equal(
            expected,
            SettingsForm.FormatIdleScanSchedule(
                new IdleScanSchedule(enabled, interval, unit)));
    }

    [Fact]
    public void ScanRootDialog_AddsNormalizedExtensionsToTheWhitelist()
    {
        using var dialog = new ScanRootDialog(Path.GetTempPath());
        dialog.CreateControl();
        var customExtensionCheckBox = Descendants(dialog)
            .OfType<CheckBox>()
            .Single(control =>
                control.AccessibleName == "扫描策略 自定义扩展名");
        var whitelistInput = Descendants(dialog)
            .OfType<TextBox>()
            .Single(control => control.AccessibleName == "白名单扩展名输入");

        customExtensionCheckBox.Checked = true;
        whitelistInput.Text = "MP4, *.mov, .MP4";
        dialog.AddExtensions(["MP4", "*.mov", ".MP4"]);

        Assert.True(dialog.IsWhitelistSelected);
        Assert.True(whitelistInput.Enabled);
        Assert.Equal([".mov", ".mp4"], dialog.ExtensionWhitelist);
        Assert.Equal(ScanFileFilter.AllFileTypes, dialog.FileTypeFilters);
    }

    [Fact]
    public void OssProfileDialog_NeverPrefillsOrRevealsTheStoredSecret()
    {
        var now = DateTimeOffset.UtcNow;
        var profile = new ObjectStorageProfile(
            Guid.NewGuid(),
            "主 OSS",
            ObjectStorageProvider.AliyunOss,
            "oss-cn-hangzhou.aliyuncs.com",
            "cdsi-assets",
            "cn-hangzhou",
            true,
            "access-key-id",
            now,
            now);
        using var form = new OssProfileDialog(profile);
        form.CreateControl();

        var secretTextBox = Descendants(form)
            .OfType<TextBox>()
            .Single(control => control.AccessibleName == "AccessKey Secret");

        Assert.True(secretTextBox.UseSystemPasswordChar);
        Assert.Empty(secretTextBox.Text);
        Assert.Null(form.CreateRequest().AccessKeySecret);
    }

    [Fact]
    public void OssProfileDialog_OffersAllBackupProviders()
    {
        using var form = new OssProfileDialog();
        form.CreateControl();
        var providerComboBox = Descendants(form)
            .OfType<ComboBox>()
            .Single(control => control.AccessibleName == "提供商");

        Assert.Equal(["阿里云 OSS", "七牛云 Kodo", "腾讯云 COS"], providerComboBox.Items
            .Cast<object>()
            .Select(item => item.ToString() ?? string.Empty)
            .ToArray());

        providerComboBox.SelectedIndex = 1;
        var request = form.CreateRequest();
        Assert.Equal(ObjectStorageProvider.QiniuKodo, request.Provider);
        Assert.Equal("s3.cn-east-1.qiniucs.com", request.Endpoint);
        Assert.Equal("cn-east-1", request.Region);

        providerComboBox.SelectedIndex = 2;
        request = form.CreateRequest();
        Assert.Equal(ObjectStorageProvider.TencentCos, request.Provider);
        Assert.Equal("cos.ap-guangzhou.myqcloud.com", request.Endpoint);
        Assert.Equal("ap-guangzhou", request.Region);
    }

    [Fact]
    public void OpenWebSourceDialog_NeverPrefillsOrRevealsTheStoredSecret()
    {
        var now = DateTimeOffset.UtcNow;
        var source = new CDSI.Agent.Core.OpenWeb.OpenWebSource(
            Guid.NewGuid(),
            "主站",
            "example.com",
            "editor",
            true,
            now,
            now);
        using var form = new OpenWebSourceDialog(source);
        form.CreateControl();

        var secretTextBox = Descendants(form)
            .OfType<TextBox>()
            .Single(control => control.AccessibleName == "WordPress 应用程序密码");

        Assert.True(secretTextBox.UseSystemPasswordChar);
        Assert.Empty(secretTextBox.Text);
        Assert.Null(form.CreateRequest().ApplicationPassword);
    }

    [Fact]
    public void GitProfileDialog_SupportsProvidersAndDoesNotRevealPasswords()
    {
        var now = DateTimeOffset.UtcNow;
        var profile = new CDSI.Agent.Core.Git.GitProfile(
            Guid.NewGuid(),
            "主仓库",
            CDSI.Agent.Core.Git.GitHostingProvider.Gitee,
            "https://gitee.com/cdsi-project/beacon.git",
            "master",
            CDSI.Agent.Core.Git.GitAuthenticationMethod.Password,
            "cdsi-project",
            null,
            true,
            now,
            now);
        using var form = new GitProfileDialog(profile);
        form.CreateControl();

        var passwordTextBox = Descendants(form)
            .OfType<TextBox>()
            .Single(control => control.AccessibleName == "Git 密码");
        var openWebsiteButton = Descendants(form)
            .OfType<Button>()
            .Single(control => control.AccessibleName == "打开 Git 托管平台网站");
        var providers = GitProfileDialog.ProviderOptions
            .Select(option => option.Value)
            .ToArray();

        Assert.Equal(
            [
                CDSI.Agent.Core.Git.GitHostingProvider.GitHub,
                CDSI.Agent.Core.Git.GitHostingProvider.Gitee
            ],
            providers);
        Assert.Equal(
            [
                CDSI.Agent.Core.Git.GitAuthenticationMethod.Password,
                CDSI.Agent.Core.Git.GitAuthenticationMethod.Ssh
            ],
            GitProfileDialog.AuthenticationOptions.Select(option => option.Value));
        Assert.True(openWebsiteButton.Enabled);
        Assert.True(passwordTextBox.UseSystemPasswordChar);
        Assert.Empty(passwordTextBox.Text);
        Assert.Null(form.CreateRequest().Password);
        Assert.Equal(
            CDSI.Agent.Core.Git.GitAuthenticationMethod.Password,
            form.CreateRequest().AuthenticationMethod);
        Assert.Equal(
            CDSI.Agent.Core.Git.GitHostingProvider.Gitee,
            form.CreateRequest().Provider);
    }

    [Fact]
    public void GitProfileDialog_SshMode_HidesPasswordRowsAndKeepsPublicKeyRow()
    {
        var now = DateTimeOffset.UtcNow;
        var profile = new CDSI.Agent.Core.Git.GitProfile(
            Guid.NewGuid(),
            "SSH 仓库",
            CDSI.Agent.Core.Git.GitHostingProvider.GitHub,
            "git@github.com:cdsi-project/Beacon.git",
            "main",
            CDSI.Agent.Core.Git.GitAuthenticationMethod.Ssh,
            string.Empty,
            @"C:\Users\creator\.ssh\id_ed25519.pub",
            true,
            now,
            now);
        using var form = new GitProfileDialog(profile);
        form.CreateControl();

        var layout = Descendants(form)
            .OfType<TableLayoutPanel>()
            .Single(panel => panel.ColumnCount == 3 && panel.RowCount == 11);
        var usernameTextBox = Descendants(form)
            .OfType<TextBox>()
            .Single(control => control.AccessibleName == "用户名");
        var passwordTextBox = Descendants(form)
            .OfType<TextBox>()
            .Single(control => control.AccessibleName == "Git 密码");
        var publicKeyTextBox = Descendants(form)
            .OfType<TextBox>()
            .Single(control => control.AccessibleName == "SSH 公钥文件");
        var generateButton = Descendants(form)
            .OfType<Button>()
            .Single(control => control.Text == "生成新密钥");

        Assert.Equal(0, layout.RowStyles[5].Height);
        Assert.Equal(0, layout.RowStyles[6].Height);
        Assert.False(usernameTextBox.Enabled);
        Assert.False(passwordTextBox.Enabled);
        Assert.True(publicKeyTextBox.Enabled);
        Assert.True(generateButton.Enabled);
    }

    [Theory]
    [InlineData(
        CDSI.Agent.Core.Git.GitHostingProvider.GitHub,
        "https://github.com/")]
    [InlineData(
        CDSI.Agent.Core.Git.GitHostingProvider.Gitee,
        "https://gitee.com/")]
    public void GitWebsiteCommand_OpensTheSelectedProvider(
        CDSI.Agent.Core.Git.GitHostingProvider provider,
        string expectedUrl)
    {
        var startInfo = SshKeySupport.CreateOpenWebsiteStartInfo(provider);

        Assert.Equal(expectedUrl, startInfo.FileName);
        Assert.True(startInfo.UseShellExecute);
    }

    [Fact]
    public void SshKeySupport_FindsOnlyACompletePreferredKeyPair()
    {
        var sshDirectory = Path.Combine(
            Path.GetTempPath(),
            $"cdsi-ssh-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(sshDirectory);
        try
        {
            File.WriteAllText(Path.Combine(sshDirectory, "id_rsa"), "private");
            File.WriteAllText(Path.Combine(sshDirectory, "id_rsa.pub"), "public");
            File.WriteAllText(Path.Combine(sshDirectory, "id_ed25519.pub"), "orphan");

            var pair = SshKeySupport.FindDefaultKeyPair(sshDirectory);

            Assert.NotNull(pair);
            Assert.EndsWith("id_rsa.pub", pair.PublicKeyPath, StringComparison.Ordinal);
            Assert.EndsWith("id_rsa", pair.PrivateKeyPath, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(sshDirectory, recursive: true);
        }
    }

    [Fact]
    public void SshKeySupport_FindsLegacyAtlasKeyPair()
    {
        var sshDirectory = Path.Combine(
            Path.GetTempPath(),
            $"cdsi-ssh-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(sshDirectory);
        try
        {
            File.WriteAllText(Path.Combine(sshDirectory, "id_ed25519_atlas"), "private");
            File.WriteAllText(Path.Combine(sshDirectory, "id_ed25519_atlas.pub"), "public");

            var pair = SshKeySupport.FindDefaultKeyPair(sshDirectory);

            Assert.NotNull(pair);
            Assert.EndsWith(
                "id_ed25519_atlas.pub",
                pair.PublicKeyPath,
                StringComparison.Ordinal);
            Assert.EndsWith(
                "id_ed25519_atlas",
                pair.PrivateKeyPath,
                StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(sshDirectory, recursive: true);
        }
    }

    [Fact]
    public void SshKeyGenerationCommand_UsesANewExplicitFile()
    {
        var sshDirectory = Path.Combine(
            Path.GetTempPath(),
            $"cdsi-ssh-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(sshDirectory);
        try
        {
            File.WriteAllText(Path.Combine(sshDirectory, "id_ed25519_beacon"), "private");
            File.WriteAllText(Path.Combine(sshDirectory, "id_ed25519_beacon.pub"), "public");
            var pair = SshKeySupport.CreateUnusedKeyPairPaths(sshDirectory);
            var startInfo = SshKeySupport.CreateSshKeyGenerationStartInfo(
                "creator@example.com",
                pair.PrivateKeyPath);

            Assert.EndsWith(
                "id_ed25519_beacon_2",
                pair.PrivateKeyPath,
                StringComparison.Ordinal);
            Assert.Equal("ssh-keygen.exe", startInfo.FileName);
            Assert.True(startInfo.UseShellExecute);
            Assert.Equal(
                [
                    "-t", "ed25519", "-C", "creator@example.com",
                    "-f", pair.PrivateKeyPath
                ],
                startInfo.ArgumentList.ToArray());
        }
        finally
        {
            Directory.Delete(sshDirectory, recursive: true);
        }
    }

    [Fact]
    public void SshKeyGenerationCommand_RejectsAnExistingTarget()
    {
        var sshDirectory = Path.Combine(
            Path.GetTempPath(),
            $"cdsi-ssh-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(sshDirectory);
        try
        {
            var privateKeyPath = Path.Combine(sshDirectory, "existing_key");
            File.WriteAllText(privateKeyPath + ".pub", "public");

            var exception = Assert.Throws<IOException>(() =>
                SshKeySupport.CreateSshKeyGenerationStartInfo(
                    "creator@example.com",
                    privateKeyPath));

            Assert.Contains("不能覆盖", exception.Message);
        }
        finally
        {
            Directory.Delete(sshDirectory, recursive: true);
        }
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }

    private static SettingsForm CreateSettingsForm(StateDatabaseWriteGate gate)
    {
        var repository = new SqliteAssetRepository(
            Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db"));
        return new SettingsForm(
            new WorkspaceApplicationService(
                repository,
                new WorkspaceProvisioner()),
            new ScanRootManagementService(repository),
            new ObjectStorageProfileService(
                repository,
                new WindowsCredentialSecretStore()),
            new OpenWebSettingsService(
                repository,
                new WindowsCredentialSecretStore()),
            new GitProfileService(
                repository,
                new WindowsCredentialSecretStore()),
            gate);
    }
}
