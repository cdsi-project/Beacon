using CDSI.Agent.Application.Workspaces;
using CDSI.Agent.Infrastructure.Persistence;

namespace CDSI.Agent.WinForms;

internal static class WorkspaceStartup
{
    internal static WorkspaceDataLocation? Resolve(
        WorkspaceDataDirectoryService service,
        string profileDirectory,
        RuntimeLogService log)
    {
        WorkspaceDataLocation? remembered = null;
        try
        {
            remembered = service.ReadLocation();
            if (remembered is not null && Directory.Exists(remembered.WorkspacePath))
            {
                WorkspaceDataDirectoryService.EnsureDataDirectory(remembered.WorkspacePath);
                return remembered;
            }
        }
        catch (Exception exception)
        {
            log.WriteError("无法读取已保存的工作目录位置", exception);
            MessageBox.Show("无法读取工作目录配置，请重新选择原工作目录。",
                "选择 Beacon 工作目录", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        if (remembered is not null)
        {
            MessageBox.Show(
                $"工作目录当前不可用：\n{remembered.WorkspacePath}\n\n请先连接对应磁盘，或重新选择原工作目录。不会在系统盘创建替代数据库。",
                "工作目录不可用", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        var legacyAsset = ExistingFile(Path.Combine(profileDirectory, "cdsi.db"));
        var legacyReader = ExistingFile(Path.Combine(profileDirectory, "reader.db"));
        var suggested = remembered?.WorkspacePath ?? service.ReadLegacyWorkspacePath() ??
            WorkspaceApplicationService.GetSuggestedDefaultPath();
        while (true)
        {
            using var setup = new FirstRunSetupForm(suggested);
            if (setup.ShowDialog() != DialogResult.OK)
            {
                return null;
            }

            suggested = setup.SelectedPath;
            try
            {
                var workspace = Path.GetFullPath(suggested);
                if (IsOnSystemDrive(workspace) && MessageBox.Show(
                        "所选目录位于系统盘，重装系统可能丢失数据库。建议选择 D 盘或其他非系统盘。\n\n仍使用此目录吗？",
                        "数据保存位置", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                        MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                {
                    continue;
                }

                var dataDirectory = WorkspaceDataDirectoryService.GetDataDirectory(workspace);
                var hasDatabases = File.Exists(Path.Combine(dataDirectory, "cdsi.db")) ||
                    File.Exists(Path.Combine(dataDirectory, "reader.db"));
                var existingState = WorkspaceDataDirectoryService.RequiresExistingState(workspace, remembered);
                if (hasDatabases)
                {
                    if (MessageBox.Show(
                            "此工作目录中已有 Beacon 数据，将直接打开已有记录。不会合并系统盘旧数据库。继续吗？",
                            "打开已有工作目录", MessageBoxButtons.OKCancel,
                            MessageBoxIcon.Information) != DialogResult.OK)
                    {
                        continue;
                    }
                }
                else if (legacyAsset is not null || legacyReader is not null)
                {
                    var choice = MessageBox.Show(
                        $"检测到旧版数据库。是否迁移到：\n{dataDirectory}\n\n将校验并复制现有数据，保留系统盘原件。\n如重装后当前记录为空，请选“否”，尝试工作目录中的旧备份。\n“取消”返回选择目录。",
                        "迁移 Beacon 数据", MessageBoxButtons.YesNoCancel,
                        MessageBoxIcon.Question, MessageBoxDefaultButton.Button1);
                    if (choice == DialogResult.Cancel)
                    {
                        continue;
                    }

                    if (choice == DialogResult.Yes)
                    {
                        service.ImportAsync(workspace, legacyAsset, legacyReader,
                            MainForm.GetApplicationVersion()).GetAwaiter().GetResult();
                        existingState = true;
                        hasDatabases = true;
                        log.WriteInformation($"旧版数据库已复制并校验：{dataDirectory}；原件保留");
                    }
                }

                if (!hasDatabases)
                {
                    TryRecoverSnapshots(service, workspace, log);
                }

                WorkspaceDataDirectoryService.EnsureDataDirectory(workspace);
                service.RememberWorkspace(workspace, existingState);
                return new WorkspaceDataLocation(1, workspace, existingState);
            }
            catch (Exception exception)
            {
                log.WriteError("无法准备工作目录数据", exception);
                MessageBox.Show(
                    $"无法准备数据，已有数据库和备份均未删除。\n\n{exception.Message}\n\n请重新选择目录；若旧数据库已损坏，可在迁移提示中选择“否”，随后从工作目录备份恢复。",
                    "工作目录设置失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    private static void TryRecoverSnapshots(
        WorkspaceDataDirectoryService service, string workspace, RuntimeLogService log)
    {
        var asset = WorkspaceDataDirectoryService.FindLatestSnapshot(workspace, reader: false);
        var reader = WorkspaceDataDirectoryService.FindLatestSnapshot(workspace, reader: true);
        if (asset is null && reader is null)
        {
            return;
        }

        var choice = MessageBox.Show(
            $"数据库不在原位置，但工作目录中有旧版自动快照。\n\n资产快照：{asset ?? "未找到"}\n\nRSS 快照：{reader ?? "未找到"}\n\n是否校验并恢复这些快照？只能恢复到各自的备份时间，之后的变化不会包含在内。恢复不修改素材文件，未找到的数据库将在后续提示中另行处理。\n\n“否”继续进入状态包恢复选择；“取消”返回。",
            "从工作目录快照恢复", MessageBoxButtons.YesNoCancel,
            MessageBoxIcon.Question, MessageBoxDefaultButton.Button1);
        if (choice == DialogResult.Cancel)
        {
            throw new OperationCanceledException("已取消快照恢复。");
        }

        if (choice != DialogResult.Yes)
        {
            return;
        }

        if (asset is not null)
        {
            WorkspaceDataDirectoryService.ValidateSnapshotAsync(asset, reader: false)
                .GetAwaiter().GetResult();
        }

        if (reader is not null)
        {
            WorkspaceDataDirectoryService.ValidateSnapshotAsync(reader, reader: true)
                .GetAwaiter().GetResult();
        }

        service.ImportAsync(workspace, asset, reader, MainForm.GetApplicationVersion())
            .GetAwaiter().GetResult();
        log.WriteInformation("已通过大小、SHA-256、SQLite 和 schema 校验恢复工作目录快照");
    }

    internal static bool IsOnSystemDrive(string path) =>
        string.Equals(Path.GetPathRoot(Path.GetFullPath(path)),
            Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)),
            StringComparison.OrdinalIgnoreCase);

    private static string? ExistingFile(string path) => File.Exists(path) ? path : null;
}
