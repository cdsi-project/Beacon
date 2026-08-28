using CDSI.Agent.Application.Assets;
using CDSI.Agent.Application.Collections;
using CDSI.Agent.Application.Fingerprints;
using CDSI.Agent.Application.Git;
using CDSI.Agent.Application.Metadata;
using CDSI.Agent.Application.OpenWeb;
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
using CDSI.Agent.Infrastructure.Security;
using CDSI.Agent.Infrastructure.Storage;

namespace CDSI.Agent.WinForms;

static class Program
{
    /// <summary>
    ///  The main entry point for the application.
    /// </summary>
    [STAThread]
    static void Main()
    {
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

            var clientIdentity = new FileClientIdentityProvider(dataDirectory)
                .GetOrCreate();

            var databasePath = Path.Combine(dataDirectory, "cdsi.db");
            var repository = new SqliteAssetRepository(databasePath);
            var localDatabaseBackupService = new LocalDatabaseBackupService(
                databasePath,
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
                applicationUpdateChecker,
                clientIdentity.Value,
                dataDirectory,
                runtimeLog);
            mainForm.Shown += (_, _) => singleInstance.StartListening(
                () => MainWindowActivator.RequestActivation(mainForm));
            System.Windows.Forms.Application.Run(mainForm);
            runtimeLog.WriteInformation("CDSI Beacon 正常退出");
        }
        catch (Exception exception)
        {
            runtimeLog?.WriteError("应用发生未处理异常", exception);
            StartupFailureReporter.Show(dataDirectory, exception);
        }
    }
}
