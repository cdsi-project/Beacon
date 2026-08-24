using CDSI.Agent.Application.Storage;
using CDSI.Agent.Application.Git;
using CDSI.Agent.Core.Collections;
using CDSI.Agent.Core.Assets;
using CDSI.Agent.Core.Git;
using CDSI.Agent.Core.Storage;
using CDSI.Agent.WinForms;

namespace CDSI.Agent.WinForms.Tests;

public sealed class AssetCollectionFormTests
{
    [Fact]
    public void CreateDialog_OffersAllFiveCollectionTypes()
    {
        using var form = new AssetCollectionDialog();

        Assert.Equal(5, AssetCollectionDialog.CollectionTypeChoices.Count);
        Assert.Equal(
            Enum.GetValues<AssetCollectionType>(),
            AssetCollectionDialog.CollectionTypeChoices.Select(choice => choice.Type));
        Assert.Equal(AssetCollectionType.Mixed, form.CollectionType);
    }

    [Fact]
    public void EditDialog_PrefillsEditableDetailsWithoutChangingBackupBindings()
    {
        using var form = new AssetCollectionDialog(
            "纪录片项目",
            AssetCollectionType.Video);
        form.CreateControl();

        var nameTextBox = Assert.Single(
            Descendants(form).OfType<TextBox>(),
            textBox => textBox.AccessibleName == "项目名称");
        var typeComboBox = Assert.Single(
            Descendants(form).OfType<ComboBox>(),
            comboBox => comboBox.AccessibleName == "项目类型");

        Assert.Equal("编辑项目", form.Text);
        Assert.Equal("纪录片项目", nameTextBox.Text);
        Assert.Equal(AssetCollectionType.Video, form.CollectionType);
        Assert.NotNull(typeComboBox.SelectedItem);
        Assert.Contains(
            Descendants(form).OfType<Button>(),
            button => button.Text == "保存");
        Assert.DoesNotContain(
            Descendants(form),
            control => control.AccessibleName == "开启云端备份" ||
                control.AccessibleName == "云端备份配置列表");
    }

    [Fact]
    public void CreateDialog_AllowsZeroOneOrMultipleCloudBackupProfiles()
    {
        var profiles = new[]
        {
            CreateBackupProfile("阿里主存储", ObjectStorageProvider.AliyunOss),
            CreateBackupProfile("腾讯归档", ObjectStorageProvider.TencentCos),
            CreateBackupProfile("七牛分发", ObjectStorageProvider.QiniuKodo)
        };
        using var form = new AssetCollectionDialog(profiles);
        form.CreateControl();
        var enableBackup = Assert.Single(Descendants(form).OfType<CheckBox>(),
            checkBox => checkBox.AccessibleName == "开启云端备份");
        var backupList = Assert.Single(Descendants(form).OfType<CheckedListBox>(),
            list => list.AccessibleName == "云端备份配置列表");

        Assert.False(enableBackup.Checked);
        Assert.False(backupList.Enabled);
        Assert.Empty(form.BackupProfileIds);
        Assert.Equal(3, backupList.Items.Count);
        Assert.Contains("阿里云 OSS · 阿里主存储", backupList.GetItemText(backupList.Items[0]));
        Assert.Contains("腾讯云 COS · 腾讯归档", backupList.GetItemText(backupList.Items[1]));
        Assert.Contains("七牛云 Kodo · 七牛分发", backupList.GetItemText(backupList.Items[2]));

        enableBackup.Checked = true;
        backupList.SetItemChecked(0, true);
        Assert.Equal([profiles[0].Profile.Id], form.BackupProfileIds);
        backupList.SetItemChecked(2, true);
        Assert.Equal(
            [profiles[0].Profile.Id, profiles[2].Profile.Id],
            form.BackupProfileIds);
    }

    [Fact]
    public void ProjectBackupBinding_RestrictsSyncToTheBoundProfile()
    {
        var profiles = new[]
        {
            CreateBackupProfile("阿里主存储", ObjectStorageProvider.AliyunOss),
            CreateBackupProfile("腾讯归档", ObjectStorageProvider.TencentCos),
            CreateBackupProfile("七牛分发", ObjectStorageProvider.QiniuKodo)
        };

        var selected = MainForm.SelectBackupProfiles(
            profiles,
            [profiles[0].Profile.Id, profiles[2].Profile.Id]);

        Assert.Equal(
            [profiles[0].Profile.Id, profiles[2].Profile.Id],
            selected.Select(profile => profile.Profile.Id));
        Assert.Equal(3, MainForm.SelectBackupProfiles(profiles, []).Count);
    }

    [Fact]
    public void ProjectBackupTarget_FormatsDisabledSingleAndMultipleStates()
    {
        var project = CreateProject("项目 A");
        Assert.Equal("未开启", MainForm.FormatProjectBackupTarget(project));

        project = project with
        {
            BackupTargets =
            [
                new(
                    Guid.NewGuid(),
                    "阿里主存储",
                    ObjectStorageProvider.AliyunOss)
            ]
        };
        Assert.Equal(
            "阿里云 OSS · 阿里主存储",
            MainForm.FormatProjectBackupTarget(project));

        project = project with
        {
            BackupTargets =
            [
                .. project.BackupTargets,
                new(
                    Guid.NewGuid(),
                    "腾讯归档",
                    ObjectStorageProvider.TencentCos)
            ]
        };
        var multiple = MainForm.FormatProjectBackupTarget(project);
        Assert.Contains("2 个目标", multiple);
        Assert.Contains("阿里云 OSS", multiple);
        Assert.Contains("腾讯云 COS", multiple);
    }

    [Fact]
    public void CollectionLayout_KeepsListsInSeparateResizablePanes()
    {
        using var collectionGrid = new DataGridView();
        using var memberGrid = new DataGridView();
        using var createButton = new Button { Text = "新建项目" };
        using var syncButton = new Button { Text = "同步到云端" };
        using var layout = MainForm.CreateAssetCollectionLayout(
            collectionGrid,
            memberGrid,
            createButton,
            syncButton);
        layout.Size = new Size(1100, 520);
        layout.CreateControl();
        layout.PerformLayout();

        var split = Assert.Single(Descendants(layout).OfType<SplitContainer>());
        Assert.Equal(Orientation.Vertical, split.Orientation);
        Assert.True(split.Panel1MinSize >= 300);
        Assert.True(split.Panel2MinSize >= 420);
        Assert.Contains(collectionGrid, Descendants(split.Panel1));
        Assert.Contains(memberGrid, Descendants(split.Panel2));
        Assert.Contains(
            Descendants(split.Panel1).OfType<Label>(),
            label => label.Text == "项目列表");
        Assert.Contains(
            Descendants(split.Panel2).OfType<Label>(),
            label => label.Text == "项目内资产");
        var toolbar = Assert.Single(
            layout.Controls.OfType<FlowLayoutPanel>());
        Assert.Equal(
            ["新建项目", "同步到云端"],
            toolbar.Controls.OfType<Button>().Select(button => button.Text));
    }

    [Fact]
    public void ProjectContextMenu_OffersSyncAndDeleteCommands()
    {
        using var contextMenu = new ContextMenuStrip();
        using var syncItem = new ToolStripMenuItem();
        using var syncToGitItem = new ToolStripMenuItem();
        using var deleteItem = new ToolStripMenuItem();

        MainForm.ConfigureProjectContextMenu(
            contextMenu,
            syncItem,
            syncToGitItem,
            deleteItem);

        Assert.Equal(4, contextMenu.Items.Count);
        Assert.Same(syncItem, contextMenu.Items[0]);
        Assert.Equal("同步到云端", syncItem.Text);
        Assert.Same(syncToGitItem, contextMenu.Items[1]);
        Assert.Equal("同步到Git", syncToGitItem.Text);
        Assert.IsType<ToolStripSeparator>(contextMenu.Items[2]);
        Assert.Same(deleteItem, contextMenu.Items[3]);
        Assert.Equal("删除项目", deleteItem.Text);
    }

    [Fact]
    public void ProjectGitMenu_ListsConfiguredRepositories()
    {
        var profiles = new[]
        {
            CreateGitProfile("仓库1", hasPassword: true),
            CreateGitProfile("仓库2", hasPassword: true),
            CreateGitProfile("凭据缺失", hasPassword: false)
        };
        using var menuItem = new ToolStripMenuItem();

        MainForm.PopulateProjectGitMenu(menuItem, profiles, canSync: true);

        Assert.Equal("同步到Git", menuItem.Text);
        Assert.True(menuItem.Enabled);
        Assert.Collection(
            menuItem.DropDownItems.Cast<ToolStripMenuItem>(),
            item =>
            {
                Assert.Equal("仓库1", item.Text);
                Assert.Equal(profiles[0].Profile.Id, item.Tag);
                Assert.True(item.Enabled);
            },
            item =>
            {
                Assert.Equal("仓库2", item.Text);
                Assert.Equal(profiles[1].Profile.Id, item.Tag);
                Assert.True(item.Enabled);
            },
            item =>
            {
                Assert.Equal("凭据缺失（凭据不可用）", item.Text);
                Assert.Equal(profiles[2].Profile.Id, item.Tag);
                Assert.False(item.Enabled);
            });
    }

    [Fact]
    public void ProjectGitMenu_ExplainsWhenNoRepositoryIsConfigured()
    {
        using var menuItem = new ToolStripMenuItem();

        MainForm.PopulateProjectGitMenu(menuItem, [], canSync: true);

        var placeholder = Assert.IsType<ToolStripMenuItem>(
            Assert.Single(menuItem.DropDownItems.Cast<ToolStripItem>()));
        Assert.Equal("尚未配置 Git 仓库", placeholder.Text);
        Assert.False(placeholder.Enabled);
    }

    [Fact]
    public void GitProjectManagementGrid_UsesResizableOperationalColumns()
    {
        using var grid = new DataGridView();

        MainForm.ConfigureGitProjectGridColumns(grid);

        Assert.Equal(
            [
                "项目",
                "类型",
                "Git仓库",
                "平台",
                "仓库地址",
                "分支",
                "最近提交",
                "资产",
                "大小",
                "本地状态",
                "同步时间"
            ],
            grid.Columns
                .Cast<DataGridViewColumn>()
                .Select(column => column.HeaderText));
        Assert.All(
            grid.Columns.Cast<DataGridViewColumn>(),
            column => Assert.Equal(
                DataGridViewTriState.True,
                column.Resizable));
    }

    [Fact]
    public void GitProjectContextMenu_OffersSyncAndNavigationCommands()
    {
        using var contextMenu = new ContextMenuStrip();
        using var syncItem = new ToolStripMenuItem();
        using var openProjectItem = new ToolStripMenuItem();
        using var openRepositoryItem = new ToolStripMenuItem();
        using var copyRepositoryUrlItem = new ToolStripMenuItem();

        MainForm.ConfigureGitProjectContextMenu(
            contextMenu,
            syncItem,
            openProjectItem,
            openRepositoryItem,
            copyRepositoryUrlItem);

        Assert.Equal(5, contextMenu.Items.Count);
        Assert.Equal("同步到Git", contextMenu.Items[0].Text);
        Assert.IsType<ToolStripSeparator>(contextMenu.Items[1]);
        Assert.Equal("打开所在项目", contextMenu.Items[2].Text);
        Assert.Equal("打开仓库", contextMenu.Items[3].Text);
        Assert.Equal("复制仓库地址", contextMenu.Items[4].Text);
    }

    [Fact]
    public void GitProjectManagement_UsesCurrentLocalDetailsAndRetainsDeletedHistory()
    {
        var project = CreateProject("当前项目名");
        var configured = CreateGitProfile("仓库1", hasPassword: true);
        var record = CreateGitSyncRecord(
            project.Id,
            configured.Profile.Id,
            "同步时项目名",
            configured.Profile.DisplayName);

        var available = Assert.Single(MainForm.CreateGitProjectManagementItems(
            [record],
            [project],
            [configured]));
        var deleted = Assert.Single(MainForm.CreateGitProjectManagementItems(
            [record],
            [],
            []));

        Assert.Equal("当前项目名", available.ProjectName);
        Assert.Equal("可用", available.LocalState);
        Assert.Equal("同步时项目名", deleted.ProjectName);
        Assert.Equal("项目、配置已删除", deleted.LocalState);
        Assert.Same(
            available,
            Assert.Single(MainForm.FilterGitProjectManagementItems(
                [available],
                "abcdef")));
    }

    [Theory]
    [InlineData(
        GitHostingProvider.GitHub,
        "https://github.com/cdsi-project/Beacon.git",
        "https://github.com/cdsi-project/Beacon")]
    [InlineData(
        GitHostingProvider.Gitee,
        "git@gitee.com:cdsi/beacon.git",
        "https://gitee.com/cdsi/beacon")]
    public void GitProjectRepositoryUrl_ConvertsConfiguredAddressesForBrowserUse(
        GitHostingProvider provider,
        string repositoryUrl,
        string expectedUrl)
    {
        var record = CreateGitSyncRecord(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "项目",
            "仓库") with
        {
            Provider = provider,
            RepositoryUrl = repositoryUrl
        };

        var converted = MainForm.TryCreateGitRepositoryBrowserUrl(record, out var url);

        Assert.True(converted);
        Assert.Equal(expectedUrl, url);
    }

    [Fact]
    public void CollectionMemberContextMenu_OffersRemoveFromProject()
    {
        using var contextMenu = new ContextMenuStrip();
        using var removeItem = new ToolStripMenuItem();

        MainForm.ConfigureCollectionMemberContextMenu(
            contextMenu,
            removeItem);

        Assert.Single(contextMenu.Items);
        Assert.Same(removeItem, contextMenu.Items[0]);
        Assert.Equal("移出项目", removeItem.Text);
    }

    [Fact]
    public void AddToProjectMenu_ShowsThreeProjectsThenMore()
    {
        var projects = Enumerable.Range(1, 4)
            .Select(index => CreateProject($"项目 {index}"))
            .ToArray();
        using var menuItem = new ToolStripMenuItem();

        MainForm.PopulateAddToProjectMenu(menuItem, projects, selectedAssetCount: 2);

        Assert.Equal("加入项目 (2)", menuItem.Text);
        Assert.True(menuItem.Enabled);
        Assert.Collection(
            menuItem.DropDownItems.Cast<ToolStripItem>(),
            item => Assert.Equal("新建项目", item.Text),
            item => Assert.IsType<ToolStripSeparator>(item),
            item => Assert.Equal(projects[0].Id, item.Tag),
            item => Assert.Equal(projects[1].Id, item.Tag),
            item => Assert.Equal(projects[2].Id, item.Tag),
            item => Assert.IsType<ToolStripSeparator>(item),
            item => Assert.Equal("更多...", item.Text));

        MainForm.PopulateAddToProjectMenu(
            menuItem,
            projects.Take(3).ToArray(),
            selectedAssetCount: 1);

        Assert.Equal(5, menuItem.DropDownItems.Count);
        Assert.DoesNotContain(
            menuItem.DropDownItems.Cast<ToolStripItem>(),
            item => item.Text == "更多...");
    }

    [Fact]
    public void AddToProjectMenu_OffersProjectCreationWhenEmpty()
    {
        using var menuItem = new ToolStripMenuItem();

        MainForm.PopulateAddToProjectMenu(
            menuItem,
            [],
            selectedAssetCount: 1);

        var createItem = Assert.Single(
            menuItem.DropDownItems.Cast<ToolStripItem>());
        Assert.Equal("新建项目", createItem.Text);
    }

    [Fact]
    public void SyncToProjectMenu_ListsOnlyCommonProjectsAndThenMore()
    {
        var projects = Enumerable.Range(1, 4)
            .Select(index => CreateProject($"项目 {index}"))
            .ToArray();
        var firstAsset = CreateAsset(
            "first.mp4",
            projects.Select(project => project.Name).ToArray());
        var secondAsset = CreateAsset(
            "second.mp4",
            projects.Select(project => project.Name).ToArray());
        var commonProjects = MainForm.FindCommonProjects(
            projects,
            [firstAsset, secondAsset]);
        using var menuItem = new ToolStripMenuItem();

        MainForm.PopulateSyncToProjectMenu(
            menuItem,
            commonProjects,
            selectedAssetCount: 2);

        Assert.Equal("同步到云端 (2)", menuItem.Text);
        Assert.Collection(
            menuItem.DropDownItems.Cast<ToolStripItem>(),
            item => Assert.Equal(projects[0].Id, item.Tag),
            item => Assert.Equal(projects[1].Id, item.Tag),
            item => Assert.Equal(projects[2].Id, item.Tag),
            item => Assert.IsType<ToolStripSeparator>(item),
            item => Assert.Equal("更多...", item.Text));
    }

    [Fact]
    public void SyncToProjectMenu_RequiresJoiningAProjectWhenNoneIsCommon()
    {
        var projects = new[] { CreateProject("项目 1"), CreateProject("项目 2") };
        var selectedAssets = new[]
        {
            CreateAsset("first.mp4", ["项目 1"]),
            CreateAsset("second.mp4", ["项目 2"])
        };
        var commonProjects = MainForm.FindCommonProjects(projects, selectedAssets);
        using var menuItem = new ToolStripMenuItem();

        MainForm.PopulateSyncToProjectMenu(
            menuItem,
            commonProjects,
            selectedAssets.Length);

        Assert.Empty(commonProjects);
        var action = Assert.Single(
            menuItem.DropDownItems.Cast<ToolStripItem>());
        Assert.Equal("加入项目并备份...", action.Text);
    }

    [Fact]
    public void AddAndSyncSelection_OffersExistingOrNewProject()
    {
        using var form = new AssetCollectionSelectionForm(
            [CreateProject("现有项目")],
            selectedAssetCount: 2,
            AssetCollectionSelectionPurpose.AddAndSync);
        form.CreateControl();
        var comboBox = Assert.Single(Descendants(form).OfType<ComboBox>());

        Assert.Equal("加入项目并备份", form.Text);
        Assert.Equal(2, comboBox.Items.Count);
        Assert.Equal("新建项目...", comboBox.GetItemText(comboBox.Items[1]));
        comboBox.SelectedIndex = 1;
        Assert.True(form.CreateNewProject);
        Assert.Null(form.SelectedCollectionId);
    }

    [Fact]
    public void OpenSelection_UsesProjectNavigationWording()
    {
        using var form = new AssetCollectionSelectionForm(
            [CreateProject("项目 A"), CreateProject("项目 B")],
            selectedAssetCount: 1,
            AssetCollectionSelectionPurpose.Open);
        form.CreateControl();

        Assert.Equal("打开所在项目", form.Text);
        Assert.Contains(
            Descendants(form).OfType<Label>(),
            label => label.Text == "选择要打开的所在项目");
        Assert.Contains(
            Descendants(form).OfType<Button>(),
            button => button.Text == "打开");
    }

    [Fact]
    public void ProjectNavigation_FindsMembershipAndSelectsTheAsset()
    {
        var matchingProject = CreateProject("项目 A");
        var projects = new[] { matchingProject, CreateProject("项目 B") };
        var asset = CreateAsset("video.mp4", ["项目 a"]);
        var otherAsset = CreateAsset("other.mp4", ["项目 A"]);

        var matching = MainForm.FindProjectsForAsset(projects, asset);
        using var grid = new DataGridView { AllowUserToAddRows = false };
        grid.Columns.Add("File", "文件");
        var otherRow = grid.Rows.Add(otherAsset.OriginalFilename);
        grid.Rows[otherRow].Tag = new AssetCollectionMember(
            matchingProject.Id,
            otherAsset,
            DateTimeOffset.UtcNow);
        var assetRow = grid.Rows.Add(asset.OriginalFilename);
        grid.Rows[assetRow].Tag = new AssetCollectionMember(
            matchingProject.Id,
            asset,
            DateTimeOffset.UtcNow);

        var selected = MainForm.SelectProjectMember(grid, asset.AssetId);

        Assert.Equal(matchingProject.Id, Assert.Single(matching).Id);
        Assert.True(selected);
        Assert.Same(grid.Rows[assetRow], grid.CurrentRow);
        Assert.True(grid.Rows[assetRow].Selected);
        Assert.False(grid.Rows[otherRow].Selected);
    }

    [Fact]
    public void ProjectDeletionConfirmation_ListsTheScopeAndPreservesAssets()
    {
        var project = new AssetCollectionSummary(
            Guid.NewGuid(),
            "夏季视频",
            AssetCollectionType.Video,
            AssetCount: 12,
            TotalSizeBytes: 1024,
            BackedUpAssetCount: 5,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow);

        var message = MainForm.CreateProjectDeletionConfirmation(project);

        Assert.Contains("夏季视频", message);
        Assert.Contains("12 个资产", message);
        Assert.Contains("不会删除、移动或修改资产文件", message);
        Assert.Contains("不会删除已有云端备份", message);
        Assert.Contains("无法撤销", message);
    }

    [Fact]
    public void MultipleProjectDeletionConfirmation_ListsTheBatchScope()
    {
        var projects = new[]
        {
            CreateProject("项目 A") with { AssetCount = 2 },
            CreateProject("项目 B") with { AssetCount = 3 }
        };

        var message = MainForm.CreateProjectsDeletionConfirmation(projects);

        Assert.Contains("2 个项目", message);
        Assert.Contains("项目 A、项目 B", message);
        Assert.Contains("合计 5 条项目成员关系", message);
        Assert.Contains("不会删除、移动或修改资产文件", message);
        Assert.Contains("不会删除已有云端备份", message);
    }

    [Fact]
    public void CollectionMemberRemovalConfirmation_PreservesAssetsAndBackups()
    {
        var project = CreateProject("纪录片");

        var message = MainForm.CreateCollectionMemberRemovalConfirmation(
            project,
            memberCount: 3);

        Assert.Contains("3 个资产", message);
        Assert.Contains("项目“纪录片”", message);
        Assert.Contains("只移除项目成员关系", message);
        Assert.Contains("不会从全部资产中移除", message);
        Assert.Contains("不会删除已有云端备份", message);
    }

    private static AssetCollectionSummary CreateProject(string name)
    {
        return new AssetCollectionSummary(
            Guid.NewGuid(),
            name,
            AssetCollectionType.Mixed,
            AssetCount: 0,
            TotalSizeBytes: 0,
            BackedUpAssetCount: 0,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow);
    }

    private static ConfiguredObjectStorageProfile CreateBackupProfile(
        string name,
        ObjectStorageProvider provider)
    {
        var now = DateTimeOffset.UtcNow;
        return new ConfiguredObjectStorageProfile(
            new ObjectStorageProfile(
                Guid.NewGuid(),
                name,
                provider,
                "https://storage.example.com",
                "beacon-assets",
                "region-1",
                UseHttps: true,
                "access-key-id",
                now,
                now),
            HasStoredSecret: true);
    }

    private static ConfiguredGitProfile CreateGitProfile(
        string name,
        bool hasPassword)
    {
        var now = DateTimeOffset.UtcNow;
        return new ConfiguredGitProfile(
            new GitProfile(
                Guid.NewGuid(),
                name,
                GitHostingProvider.Gitee,
                $"https://gitee.com/cdsi-project/{name}.git",
                "master",
                GitAuthenticationMethod.Password,
                "cdsi-project",
                null,
                IsDefault: false,
                now,
                now),
            hasPassword);
    }

    private static GitProjectSyncRecord CreateGitSyncRecord(
        Guid projectId,
        Guid profileId,
        string projectName,
        string profileName)
    {
        return new GitProjectSyncRecord(
            projectId,
            projectName,
            AssetCollectionType.Text,
            profileId,
            profileName,
            GitHostingProvider.GitHub,
            "https://github.com/cdsi-project/articles.git",
            "main",
            "abcdef0123456789",
            SyncedFiles: 2,
            SyncedBytes: 128,
            CreatedCommit: true,
            DateTimeOffset.UtcNow);
    }

    private static AssetListItem CreateAsset(
        string filename,
        IReadOnlyList<string> projectNames)
    {
        var now = DateTimeOffset.UtcNow;
        return new AssetListItem(
            Guid.NewGuid(),
            filename,
            Path.GetExtension(filename),
            "video/mp4",
            42,
            null,
            now,
            now,
            Path.Combine(Path.GetTempPath(), filename),
            AssetLocationOwnership.External,
            AssetLocationStatus.Available,
            AssetStatus.Indexed,
            HasHealthyObjectStorageBackup: false)
        {
            ProjectNames = projectNames
        };
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
}
