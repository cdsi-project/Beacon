using CDSI.Agent.Core.Collections;
using CDSI.Agent.Core.Reader;
using CDSI.Agent.Infrastructure.Persistence;
using CDSI.Agent.Infrastructure.Reader;
using Microsoft.Data.Sqlite;

namespace CDSI.Agent.Infrastructure.Tests.Persistence;

public sealed class WorkspaceDataDirectoryServiceTests
{
    [Fact]
    public async Task Import_PreservesProjectAndReaderRecordsIncludingUncheckpointedWal()
    {
        using var directory = new TestDirectory();
        var source = Path.Combine(directory.Path, "Profile");
        var (assetPath, readerPath, projectId, feedId) = await CreateDatabasesAsync(source);
        using var writer = new SqliteConnection($"Data Source={assetPath};Pooling=False");
        await writer.OpenAsync();
        using var command = writer.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=WAL; PRAGMA wal_autocheckpoint=0; " +
            "UPDATE asset_collections SET name = 'WAL 中的项目' WHERE id = $id;";
        command.Parameters.AddWithValue("$id", projectId.ToString("D"));
        await command.ExecuteNonQueryAsync();
        Assert.True(File.Exists($"{assetPath}-wal"));

        var workspace = Path.Combine(directory.Path, "Workspace");
        var service = new WorkspaceDataDirectoryService(source);
        await service.ImportAsync(workspace, assetPath, readerPath, "0.2.22");
        var target = WorkspaceDataDirectoryService.GetDataDirectory(workspace);
        var restored = new SqliteAssetRepository(Path.Combine(target, "cdsi.db"));
        Assert.Equal("WAL 中的项目", (await restored.GetAssetCollectionAsync(projectId))?.Name);
        using var restoredReader = new SqliteReaderRepository(Path.Combine(target, "reader.db"));
        Assert.Equal(feedId, Assert.Single(await restoredReader.ListFeedsAsync()).Feed.Id);
        Assert.True(File.Exists(assetPath));
        Assert.True(File.Exists(readerPath));
        Assert.Equal("WAL 中的项目", (await new SqliteAssetRepository(assetPath)
            .GetAssetCollectionAsync(projectId))?.Name);
        writer.Close();
        SqliteConnection.ClearAllPools();
    }

    [Fact]
    public async Task ReinstalledProfile_ExistingWorkspaceKeepsRecordsWithoutTheLocatorOrClientId()
    {
        using var directory = new TestDirectory();
        var profile = Path.Combine(directory.Path, "OriginalProfile");
        var (asset, reader, projectId, _) = await CreateDatabasesAsync(profile);
        var workspace = Path.Combine(directory.Path, "Workspace");
        var service = new WorkspaceDataDirectoryService(profile);
        await service.ImportAsync(workspace, asset, reader, "0.2.22");
        service.MarkInitialized(workspace);

        var reinstalled = new WorkspaceDataDirectoryService(Path.Combine(directory.Path, "NewProfile"));
        Assert.Null(reinstalled.ReadLocation());
        Assert.True(WorkspaceDataDirectoryService.HasExistingState(workspace));
        reinstalled.RememberWorkspace(workspace, initialized: true);
        Assert.Equal(workspace, reinstalled.ReadLocation()?.WorkspacePath);
        var opened = new SqliteAssetRepository(Path.Combine(
            WorkspaceDataDirectoryService.GetDataDirectory(workspace), "cdsi.db"));
        Assert.NotNull(await opened.GetAssetCollectionAsync(projectId));
        SqliteConnection.ClearAllPools();
    }

    [Fact]
    public async Task Import_RefusesAnOccupiedDestinationWithoutChangingEitherDatabase()
    {
        using var directory = new TestDirectory();
        var (asset, reader, _, _) = await CreateDatabasesAsync(Path.Combine(directory.Path, "Source"));
        var workspace = Path.Combine(directory.Path, "Workspace");
        var target = WorkspaceDataDirectoryService.EnsureDataDirectory(workspace);
        var destination = Path.Combine(target, "cdsi.db");
        await File.WriteAllTextAsync(destination, "existing user data");
        var service = new WorkspaceDataDirectoryService(Path.Combine(directory.Path, "Profile"));

        await Assert.ThrowsAsync<IOException>(() => service.ImportAsync(workspace, asset, reader, "0.2.22"));

        Assert.Equal("existing user data", await File.ReadAllTextAsync(destination));
        Assert.True(File.Exists(asset));
        Assert.True(File.Exists(reader));
        Assert.False(File.Exists(Path.Combine(target, "reader.db")));
        SqliteConnection.ClearAllPools();
    }

    [Fact]
    public async Task Import_SecondDatabaseFailureDoesNotPublishAHalfPair()
    {
        using var directory = new TestDirectory();
        var (asset, reader, projectId, _) = await CreateDatabasesAsync(Path.Combine(directory.Path, "Source"));
        SqliteConnection.ClearAllPools();
        await File.WriteAllTextAsync(reader, "damaged reader");
        var workspace = Path.Combine(directory.Path, "Workspace");
        var service = new WorkspaceDataDirectoryService(Path.Combine(directory.Path, "Profile"));

        await Assert.ThrowsAnyAsync<Exception>(() => service.ImportAsync(workspace, asset, reader, "0.2.22"));

        Assert.False(Directory.Exists(WorkspaceDataDirectoryService.GetDataDirectory(workspace)));
        Assert.Equal("damaged reader", await File.ReadAllTextAsync(reader));
        Assert.NotNull(await new SqliteAssetRepository(asset).GetAssetCollectionAsync(projectId));
        SqliteConnection.ClearAllPools();
    }

    [Fact]
    public async Task Import_CancelledPreparationLeavesSourceAndLocationUnchanged()
    {
        using var directory = new TestDirectory();
        var (asset, reader, _, _) = await CreateDatabasesAsync(Path.Combine(directory.Path, "Source"));
        var oldWorkspace = Path.Combine(directory.Path, "OldWorkspace");
        var newWorkspace = Path.Combine(directory.Path, "NewWorkspace");
        var service = new WorkspaceDataDirectoryService(Path.Combine(directory.Path, "Profile"));
        service.RememberWorkspace(oldWorkspace, initialized: true);

        await Assert.ThrowsAsync<OperationCanceledException>(() => service.ImportAsync(
            newWorkspace, asset, reader, "0.2.22", _ => throw new OperationCanceledException()));

        Assert.Equal(oldWorkspace, service.ReadLocation()?.WorkspacePath);
        Assert.False(Directory.Exists(WorkspaceDataDirectoryService.GetDataDirectory(newWorkspace)));
        Assert.True(File.Exists(asset));
        Assert.True(File.Exists(reader));
        SqliteConnection.ClearAllPools();
    }

    [Fact]
    public async Task Snapshots_AreDiscoverableAndValidatedWithoutTheOriginalDatabases()
    {
        using var directory = new TestDirectory();
        var (asset, reader, projectId, _) = await CreateDatabasesAsync(Path.Combine(directory.Path, "OldProfile"));
        var workspace = Path.Combine(directory.Path, "Workspace");
        var assetBackup = await new LocalDatabaseBackupService(asset, "0.2.21")
            .CreateSnapshotAsync(workspace);
        var readerBackup = await new LocalDatabaseBackupService(reader, "0.2.21", "Reader")
            .CreateSnapshotAsync(workspace);
        SqliteConnection.ClearAllPools();
        File.Delete(asset);
        File.Delete(reader);

        Assert.True(WorkspaceDataDirectoryService.HasExistingState(workspace));
        Assert.Equal(assetBackup.SnapshotPath, WorkspaceDataDirectoryService.FindLatestSnapshot(workspace, false));
        Assert.Equal(readerBackup.SnapshotPath, WorkspaceDataDirectoryService.FindLatestSnapshot(workspace, true));
        await WorkspaceDataDirectoryService.ValidateSnapshotAsync(assetBackup.SnapshotPath, false);
        await WorkspaceDataDirectoryService.ValidateSnapshotAsync(readerBackup.SnapshotPath, true);
        var service = new WorkspaceDataDirectoryService(Path.Combine(directory.Path, "ReinstalledProfile"));
        await service.ImportAsync(workspace, assetBackup.SnapshotPath, readerBackup.SnapshotPath, "0.2.22");
        var recovered = new SqliteAssetRepository(Path.Combine(
            WorkspaceDataDirectoryService.GetDataDirectory(workspace), "cdsi.db"));
        Assert.NotNull(await recovered.GetAssetCollectionAsync(projectId));
        Assert.True(File.Exists(assetBackup.SnapshotPath));
        SqliteConnection.ClearAllPools();
    }

    [Fact]
    public async Task SnapshotValidation_RejectsTamperingAndWrongDatabaseRole()
    {
        using var directory = new TestDirectory();
        var (asset, _, _, _) = await CreateDatabasesAsync(Path.Combine(directory.Path, "Source"));
        var result = await new LocalDatabaseBackupService(asset, "0.2.21")
            .CreateSnapshotAsync(Path.Combine(directory.Path, "Workspace"));
        await Assert.ThrowsAnyAsync<Exception>(() =>
            WorkspaceDataDirectoryService.ValidateSnapshotAsync(result.SnapshotPath, reader: true));
        await File.AppendAllTextAsync(result.SnapshotPath, "tampered");
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            WorkspaceDataDirectoryService.ValidateSnapshotAsync(result.SnapshotPath, reader: false));
        SqliteConnection.ClearAllPools();
    }

    [Fact]
    public void Location_MissingDriveDoesNotCreateReplacementDatabaseDirectories()
    {
        using var directory = new TestDirectory();
        var absentWorkspace = Path.Combine(directory.Path, "DisconnectedDrive", "Workspace");
        var service = new WorkspaceDataDirectoryService(Path.Combine(directory.Path, "Profile"));
        service.RememberWorkspace(absentWorkspace, initialized: true);

        Assert.True(service.ReadLocation()?.Initialized);
        Assert.False(Directory.Exists(absentWorkspace));
        Assert.False(Directory.Exists(WorkspaceDataDirectoryService.GetDataDirectory(absentWorkspace)));
    }

    [Fact]
    public void MissingOriginalWorkspace_RetainsInitializedEvidenceAfterExplicitReselection()
    {
        using var directory = new TestDirectory();
        var absentWorkspace = Path.Combine(directory.Path, "DeletedWorkspace");
        var remembered = new WorkspaceDataLocation(1, absentWorkspace, Initialized: true);
        Assert.True(WorkspaceDataDirectoryService.RequiresExistingState(absentWorkspace, remembered));
        Assert.False(WorkspaceDataDirectoryService.RequiresExistingState(
            Path.Combine(directory.Path, "IntentionallyNewWorkspace"), remembered));
        Assert.False(Directory.Exists(absentWorkspace));
    }

    [Fact]
    public void ReaderOnlySnapshot_RequiresRecoveryInsteadOfSilentEmptyInitialization()
    {
        using var directory = new TestDirectory();
        var workspace = Path.Combine(directory.Path, "Workspace");
        var backupDirectory = Path.Combine(workspace, "System", "DatabaseBackups", "Reader");
        Directory.CreateDirectory(backupDirectory);
        File.WriteAllText(Path.Combine(backupDirectory, "cdsi-old.db"), "fixture");
        Assert.True(WorkspaceDataDirectoryService.RequiresExistingState(workspace, remembered: null));
    }

    [Fact]
    public async Task StagedReconfiguration_InvalidDatabaseCannotBecomeThePublishedPair()
    {
        using var directory = new TestDirectory();
        var (asset, reader, _, _) = await CreateDatabasesAsync(Path.Combine(directory.Path, "Source"));
        var workspace = Path.Combine(directory.Path, "Workspace");
        var service = new WorkspaceDataDirectoryService(Path.Combine(directory.Path, "Profile"));
        await Assert.ThrowsAnyAsync<Exception>(() => service.ImportAsync(
            workspace, asset, reader, "0.2.22", async staging =>
                await File.WriteAllTextAsync(Path.Combine(staging, "reader.db"), "invalid stage")));
        Assert.False(Directory.Exists(WorkspaceDataDirectoryService.GetDataDirectory(workspace)));
        Assert.True(File.Exists(asset));
        Assert.True(File.Exists(reader));
        SqliteConnection.ClearAllPools();
    }

    private static async Task<(string Asset, string Reader, Guid ProjectId, Guid FeedId)>
        CreateDatabasesAsync(string source)
    {
        var assetPath = Path.Combine(source, "cdsi.db");
        var readerPath = Path.Combine(source, "reader.db");
        var repository = new SqliteAssetRepository(assetPath);
        await repository.InitializeAsync();
        var now = DateTimeOffset.UtcNow;
        var projectId = Guid.NewGuid();
        await repository.CreateAssetCollectionAsync(new AssetCollection(
            projectId, "保留的项目", AssetCollectionType.Mixed, now, now));
        using var reader = new SqliteReaderRepository(readerPath);
        await reader.InitializeAsync();
        var feedId = Guid.NewGuid();
        var feed = new ReaderFeed(feedId, "保留的订阅", "https://example.com/rss",
            "https://example.com", "description", ReaderFeedType.Rss20, "folder", null,
            null, now, now, now, now, true, false, null);
        var log = new ReaderFetchLog(Guid.NewGuid(), feedId, now, now, 200,
            "Success", 0, 100, 10, null);
        await reader.SaveFetchedFeedAsync(feed, [], log);
        return (assetPath, readerPath, projectId, feedId);
    }
}
