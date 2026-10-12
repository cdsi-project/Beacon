using CDSI.Agent.Application.Assets;
using CDSI.Agent.Application.Collections;
using CDSI.Agent.Application.Scanning;
using CDSI.Agent.Core.Collections;
using CDSI.Agent.Infrastructure.FileSystem;
using CDSI.Agent.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace CDSI.Agent.IntegrationTests.Assets;

public sealed class SelectedFileImportServiceTests
{
    [Fact]
    public async Task Import_RegistersOnlySelectedFilesAndDoesNotCreateScanRootsOrChangeFiles()
    {
        using var directory = new TestDirectory();
        var selected = Path.Combine(directory.Path, "中文 素材 & draft.mp4");
        var other = Path.Combine(directory.Path, "unselected.txt");
        await File.WriteAllBytesAsync(selected, [1, 2, 3]);
        await File.WriteAllTextAsync(other, "untouched");
        var modified = File.GetLastWriteTimeUtc(selected);
        var repository = new SqliteAssetRepository(Path.Combine(directory.Path, "state", "cdsi.db"));
        await repository.InitializeAsync();
        var service = new SelectedFileImportService(repository, new LocalFileInspector());

        var result = await service.ImportAsync([selected, selected], []);
        var first = Assert.Single(await repository.ListAssetsAsync(100));
        var repeated = await service.ImportAsync([selected], []);

        Assert.Equal(1, result.ImportedCount);
        Assert.Equal(0, result.FailedCount);
        Assert.Equal(first.AssetId, Assert.Single(repeated.Items).AssetId);
        Assert.Single(await repository.ListAssetsAsync(100));
        Assert.Empty(await repository.ListScanRootsAsync());
        Assert.Equal(selected, first.Path);
        Assert.Null(first.Sha256);
        Assert.Equal([1, 2, 3], await File.ReadAllBytesAsync(selected));
        Assert.Equal(modified, File.GetLastWriteTimeUtc(selected));
        Assert.Equal("untouched", await File.ReadAllTextAsync(other));
        SqliteConnection.ClearAllPools();
    }

    [Fact]
    public async Task ExplicitImport_RestoresHiddenAssetWithItsIdentityAndProjectMembership()
    {
        using var directory = new TestDirectory();
        var source = Directory.CreateDirectory(Path.Combine(directory.Path, "source")).FullName;
        var file = Path.Combine(source, "article.md");
        await File.WriteAllTextAsync(file, "# My article");
        var repository = new SqliteAssetRepository(Path.Combine(directory.Path, "state", "cdsi.db"));
        await repository.InitializeAsync();
        var scanner = new ScanApplicationService(new FileSystemScanner(), repository);
        await scanner.ScanDirectoryAsync(source);
        var original = Assert.Single(await repository.ListAssetsAsync(100));
        var projects = new AssetCollectionService(repository);
        var project = await projects.CreateAsync("My project", AssetCollectionType.Text);
        await projects.AddAssetsAsync(project.Id, [original.AssetId]);
        await repository.HideAssetsFromListAsync([original.AssetId], DateTimeOffset.UtcNow);
        await scanner.ScanDirectoryAsync(source);
        Assert.Empty(await repository.ListAssetsAsync(100));

        var result = await new SelectedFileImportService(repository, new LocalFileInspector())
            .ImportAsync([file], []);

        Assert.Equal(original.AssetId, Assert.Single(result.Items).AssetId);
        Assert.Equal(original.AssetId, Assert.Single(await repository.ListAssetsAsync(100)).AssetId);
        Assert.Equal(original.AssetId, Assert.Single(await projects.GetMembersAsync(project.Id)).Asset.AssetId);
        Assert.Equal("# My article", await File.ReadAllTextAsync(file));
        SqliteConnection.ClearAllPools();
    }

    [Fact]
    public async Task Import_RejectsDirectoriesMissingFilesAndProtectedDataButContinuesOtherFiles()
    {
        using var directory = new TestDirectory();
        var system = Directory.CreateDirectory(Path.Combine(directory.Path, "System")).FullName;
        var protectedFile = Path.Combine(system, "cdsi.db");
        var allowedSibling = Path.Combine(directory.Path, "System-copy", "ok.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(allowedSibling)!);
        await File.WriteAllTextAsync(protectedFile, "protected");
        await File.WriteAllTextAsync(allowedSibling, "allowed");
        var repository = new SqliteAssetRepository(Path.Combine(directory.Path, "state", "cdsi.db"));
        await repository.InitializeAsync();
        var service = new SelectedFileImportService(repository, new LocalFileInspector());

        var result = await service.ImportAsync([
            system, protectedFile, Path.Combine(directory.Path, "missing.txt"), "relative.txt", allowedSibling
        ], [system]);

        Assert.Equal(1, result.ImportedCount);
        Assert.Equal(4, result.FailedCount);
        Assert.Equal(allowedSibling, Assert.Single(await repository.ListAssetsAsync(100)).Path);
        Assert.Empty(await repository.ListScanRootsAsync());
        Assert.Equal("protected", await File.ReadAllTextAsync(protectedFile));
        SqliteConnection.ClearAllPools();
    }

    [Fact]
    public async Task Import_CancellationKeepsCompletedRegistrationAndDoesNotRegisterTheRest()
    {
        using var directory = new TestDirectory();
        var first = Path.Combine(directory.Path, "first.txt");
        var second = Path.Combine(directory.Path, "second.txt");
        await File.WriteAllTextAsync(first, "first");
        await File.WriteAllTextAsync(second, "second");
        var repository = new SqliteAssetRepository(Path.Combine(directory.Path, "state", "cdsi.db"));
        await repository.InitializeAsync();
        using var cancellation = new CancellationTokenSource();
        var progress = new InlineProgress(path => { if (path == second) cancellation.Cancel(); });

        var result = await new SelectedFileImportService(repository, new LocalFileInspector())
            .ImportAsync([first, second], [], progress, cancellation.Token);

        Assert.True(result.Cancelled);
        Assert.Equal(1, result.ImportedCount);
        Assert.Equal(first, Assert.Single(await repository.ListAssetsAsync(100)).Path);
        SqliteConnection.ClearAllPools();
    }

    private sealed class InlineProgress(Action<string> action) : IProgress<string>
    {
        public void Report(string value) => action(value);
    }
}
