using System.Diagnostics;
using CDSI.Agent.Application.Assets;
using CDSI.Agent.Application.Collections;
using CDSI.Agent.Application.Fingerprints;
using CDSI.Agent.Application.Git;
using CDSI.Agent.Application.Metadata;
using CDSI.Agent.Application.OpenWeb;
using CDSI.Agent.Application.Reader;
using CDSI.Agent.Application.Scanning;
using CDSI.Agent.Application.Storage;
using CDSI.Agent.Application.Transfers;
using CDSI.Agent.Application.Workspaces;
using CDSI.Agent.Core.Abstractions;
using CDSI.Agent.Core.Storage;
using CDSI.Agent.Infrastructure.FileSystem;
using CDSI.Agent.Infrastructure.Fingerprints;
using CDSI.Agent.Infrastructure.Git;
using CDSI.Agent.Infrastructure.Identity;
using CDSI.Agent.Infrastructure.Metadata;
using CDSI.Agent.Infrastructure.OpenWeb;
using CDSI.Agent.Infrastructure.Persistence;
using CDSI.Agent.Infrastructure.Reader;
using CDSI.Agent.Infrastructure.Security;
using CDSI.Agent.Infrastructure.Storage;

namespace CDSI.Agent.WinForms;

[Flags]
public enum MissingStateDatabases
{
    None = 0,
    Asset = 1,
    Reader = 2
}

static class Program
{
    /// <summary>
    ///  The main entry point for the application.
    /// </summary>
    [STAThread]
    static void Main(string[] args)
    {
        if (TryRunPendingRestoreRestartHelper(args))
        {
            return;
        }

        var dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CDSI");
        RuntimeLogService? runtimeLog = null;
        try
        {
            // Retain the legacy mutex name so Atlas and Beacon cannot run together.
            using var singleInstance = new SingleInstanceCoordinator("CDSI.Atlas");
            if (!singleInstance.IsPrimaryInstance)
            {
                singleInstance.SignalPrimaryInstance();
                return;
            }

            ApplicationConfiguration.Initialize();
            runtimeLog = new RuntimeLogService(dataDirectory);

            var databasePath = Path.Combine(dataDirectory, "cdsi.db");
            var readerDatabasePath = Path.Combine(dataDirectory, "reader.db");
            var clientIdentityExistedBeforeStartup = File.Exists(Path.Combine(
                dataDirectory,
                FileClientIdentityProvider.IdentityFileName));
            var pendingStateRestoreService = new PendingStateRestoreService(
                dataDirectory,
                databasePath,
                readerDatabasePath);
            StateRestoreApplyResult? startupRestoreResult = null;
            string? startupRestoreWarning = null;
            string? startupRestoreSafetyBackupPath = null;
            try
            {
                startupRestoreResult = pendingStateRestoreService
                    .ApplyPendingAsync()
                    .GetAwaiter()
                    .GetResult();
                if (startupRestoreResult is not null)
                {
                    runtimeLog.WriteInformation(
                        $"Beacon 状态恢复完成；RestoreId={startupRestoreResult.RestoreId:D}；" +
                        $"BackupId={startupRestoreResult.BackupId:D}");
                }
            }
            catch (StateRestoreFailedException exception) when (exception.CurrentStateIsSafe)
            {
                runtimeLog.WriteError("状态恢复未完成，已保留操作前状态", exception);
                startupRestoreSafetyBackupPath = exception.SafetyBackupPath;
                startupRestoreWarning =
                    "状态恢复未完成，已自动保留操作前的状态。当前数据库未被更改。\n\n" +
                    "请打开“工具 > 运行日志”查看详细原因。";
            }
            catch (StateBackupValidationException exception)
            {
                runtimeLog.WriteError("待恢复状态备份验证失败", exception);
                startupRestoreWarning =
                    $"待恢复的状态备份未通过验证，当前数据未更改。\n\n{exception.Message}";
            }

            var clientIdentity = new FileClientIdentityProvider(dataDirectory)
                .GetOrCreate();
            var missingStateDatabases =
                GetMissingStateDatabases(
                    clientIdentityExistedBeforeStartup,
                    assetDatabaseExistedBeforeStartup: File.Exists(databasePath),
                    readerDatabaseExistedBeforeStartup: File.Exists(readerDatabasePath));

            var repository = new SqliteAssetRepository(databasePath);
            using var readerRepository = new SqliteReaderRepository(readerDatabasePath);
            using var readerHttpClient = ReaderHttpFeedClient.CreateHttpClient(
                MainForm.GetApplicationVersion());
            var readerService = new ReaderApplicationService(
                readerRepository,
                new ReaderHttpFeedClient(readerHttpClient, new SyndicationFeedParser()),
                new OpmlSubscriptionExchange());
            var localDatabaseBackupService = new LocalDatabaseBackupService(
                databasePath,
                MainForm.GetApplicationVersion());
            var readerDatabaseBackupService = new LocalDatabaseBackupService(
                readerDatabasePath,
                MainForm.GetApplicationVersion(),
                "Reader");
            var localStateProtectionService = new LocalStateProtectionService(
                dataDirectory,
                databasePath,
                readerDatabasePath,
                MainForm.GetApplicationVersion());
            var fingerprintEngine = new Sha256FileFingerprintService();
            var scanService = new ScanApplicationService(new FileSystemScanner(), repository);
            var workspaceProvisioner = new WorkspaceProvisioner();
            var workspaceService = new WorkspaceApplicationService(
                repository,
                workspaceProvisioner);
            var scanRootService = new ScanRootManagementService(repository);
            var volumeReconciliationService = new LocalVolumeReconciliationService(
                new WindowsLocalVolumeProvider(),
                repository);
            var secretStore = new WindowsCredentialSecretStore();
            var storageService = new ObjectStorageProfileService(
                repository,
                secretStore);
            var openWebSettingsService = new OpenWebSettingsService(
                repository,
                secretStore);
            var gitProfileService = new GitProfileService(
                repository,
                secretStore);
            var openWebPublishingService = new OpenWebArticlePublishingService(
                openWebSettingsService,
                repository,
                new LocalOpenWebArticleContentReader(),
                new WordPressArticlePublisher(new HttpClient
                {
                    Timeout = TimeSpan.FromSeconds(60)
                }));
            IObjectStorageAdapter[] objectStorageAdapters =
            [
                new AliyunOssStorageAdapter(),
                new S3CompatibleStorageAdapter(ObjectStorageProvider.QiniuKodo),
                new S3CompatibleStorageAdapter(ObjectStorageProvider.TencentCos)
            ];
            var objectStorageBackupService = new ObjectStorageBackupService(
                repository,
                repository,
                storageService,
                fingerprintEngine,
                objectStorageAdapters);
            var objectStorageRestoreService = new ObjectStorageRestoreService(
                repository,
                repository,
                repository,
                storageService,
                workspaceProvisioner,
                objectStorageAdapters);
            var objectStorageManagementService = new ObjectStorageManagementService(
                repository,
                storageService,
                objectStorageAdapters);
            var transferService = new ManagedAssetTransferService(
                repository,
                workspaceProvisioner,
                new VerifiedManagedFileTransfer());
            var assetCollectionService = new AssetCollectionService(repository);
            var gitProjectSyncService = new GitProjectSyncService(
                assetCollectionService,
                gitProfileService,
                workspaceService,
                new GitCliProjectSynchronizer(),
                repository);
            var assetTagService = new AssetTagService(repository);
            var fingerprintService = new FingerprintApplicationService(
                fingerprintEngine,
                repository);
            var metadataService = new MetadataExtractionApplicationService(
                [
                    new TagLibMetadataExtractor(),
                    new GenericMetadataExtractor()
                ],
                repository);
            using var updateHttpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(10)
            };
            var applicationUpdateChecker = new GiteeApplicationUpdateChecker(
                updateHttpClient);
            using var mainForm = new MainForm(
                scanService,
                fingerprintService,
                metadataService,
                readerService,
                workspaceService,
                scanRootService,
                volumeReconciliationService,
                storageService,
                openWebSettingsService,
                gitProfileService,
                openWebPublishingService,
                objectStorageBackupService,
                objectStorageRestoreService,
                objectStorageManagementService,
                assetCollectionService,
                gitProjectSyncService,
                assetTagService,
                transferService,
                localDatabaseBackupService,
                readerDatabaseBackupService,
                localStateProtectionService,
                applicationUpdateChecker,
                missingStateDatabases,
                clientIdentity.Value,
                dataDirectory,
                runtimeLog);
            mainForm.SetStartupStateRestoreNotification(
                startupRestoreResult,
                startupRestoreWarning,
                startupRestoreSafetyBackupPath);
            mainForm.Shown += (_, _) => singleInstance.StartListening(
                () => MainWindowActivator.RequestActivation(mainForm));
            System.Windows.Forms.Application.Run(mainForm);
            var restartForPendingStateRestore =
                mainForm.RestartForPendingStateRestore;
            runtimeLog.WriteInformation("CDSI Beacon 正常退出");
            if (restartForPendingStateRestore)
            {
                StartPendingRestoreRestartHelper(runtimeLog);
            }
        }
        catch (Exception exception)
        {
            runtimeLog?.WriteError("应用发生未处理异常", exception);
            StartupFailureReporter.Show(dataDirectory, exception);
        }
    }

    internal static MissingStateDatabases GetMissingStateDatabases(
        bool clientIdentityExistedBeforeStartup,
        bool assetDatabaseExistedBeforeStartup,
        bool readerDatabaseExistedBeforeStartup)
    {
        var existingInstallation =
            clientIdentityExistedBeforeStartup ||
            assetDatabaseExistedBeforeStartup ||
            readerDatabaseExistedBeforeStartup;
        if (!existingInstallation)
        {
            return MissingStateDatabases.None;
        }

        var missing = MissingStateDatabases.None;
        if (!assetDatabaseExistedBeforeStartup)
        {
            missing |= MissingStateDatabases.Asset;
        }

        if (!readerDatabaseExistedBeforeStartup)
        {
            missing |= MissingStateDatabases.Reader;
        }

        return missing;
    }

    internal static bool TryParsePendingRestoreRestartHelper(
        IReadOnlyList<string> args,
        out int parentProcessId)
    {
        parentProcessId = 0;
        return args.Count == 2 &&
            string.Equals(
                args[0],
                "--restart-for-pending-state-restore",
                StringComparison.Ordinal) &&
            int.TryParse(args[1], out parentProcessId) &&
            parentProcessId > 0 &&
            parentProcessId != Environment.ProcessId;
    }

    internal static ProcessStartInfo CreatePendingRestoreRestartHelperStartInfo(
        string executablePath,
        int parentProcessId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        if (parentProcessId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(parentProcessId));
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = Path.GetFullPath(executablePath),
            WorkingDirectory = AppContext.BaseDirectory,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("--restart-for-pending-state-restore");
        startInfo.ArgumentList.Add(parentProcessId.ToString());
        return startInfo;
    }

    private static bool TryRunPendingRestoreRestartHelper(string[] args)
    {
        if (!TryParsePendingRestoreRestartHelper(args, out var parentProcessId))
        {
            return false;
        }

        try
        {
            try
            {
                using var parent = Process.GetProcessById(parentProcessId);
                parent.WaitForExit();
            }
            catch (ArgumentException)
            {
                // The parent already exited before the helper opened its process handle.
            }

            var executablePath = Environment.ProcessPath ??
                System.Windows.Forms.Application.ExecutablePath;
            if (Process.Start(
                    new ProcessStartInfo
                    {
                        FileName = executablePath,
                        WorkingDirectory = AppContext.BaseDirectory,
                        UseShellExecute = true
                    }) is null)
            {
                throw new InvalidOperationException("无法启动 Beacon 进程。");
            }
        }
        catch (Exception exception)
        {
            ReportRestartHelperFailure(exception);
        }

        return true;
    }

    private static void ReportRestartHelperFailure(Exception exception)
    {
        var dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CDSI");
        try
        {
            new RuntimeLogService(dataDirectory).WriteError(
                "状态恢复重启助手无法重新启动 Beacon",
                exception);
        }
        catch
        {
            // The message box remains available if the log directory is unavailable.
        }

        ApplicationConfiguration.Initialize();
        MessageBox.Show(
            "Beacon 无法自动重新启动。待恢复状态仍已保留，请手动重新打开 Beacon。",
            "请手动重新启动 Beacon",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
    }

    private static void StartPendingRestoreRestartHelper(RuntimeLogService runtimeLog)
    {
        try
        {
            var executablePath = Environment.ProcessPath ??
                System.Windows.Forms.Application.ExecutablePath;
            if (Process.Start(CreatePendingRestoreRestartHelperStartInfo(
                    executablePath,
                    Environment.ProcessId)) is null)
            {
                throw new InvalidOperationException("无法启动状态恢复重启助手。");
            }
        }
        catch (Exception exception)
        {
            runtimeLog.WriteError("无法自动重新启动 Beacon", exception);
            MessageBox.Show(
                "状态恢复已经安排，但 Beacon 无法自动重新启动。请手动重新打开 Beacon，恢复将在启动时继续。",
                "请手动重新启动 Beacon",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }
}
