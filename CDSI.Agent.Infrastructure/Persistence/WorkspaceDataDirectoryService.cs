using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace CDSI.Agent.Infrastructure.Persistence;

/// <summary>Locates persistent workspace state without putting databases in the user profile.</summary>
public sealed class WorkspaceDataDirectoryService
{
    public const string LocationFileName = "workspace-location.json";
    public const string StateMarkerFileName = "beacon-data.json";
    private readonly string _profileDirectory;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public WorkspaceDataDirectoryService(string profileDirectory)
    {
        _profileDirectory = Path.GetFullPath(profileDirectory);
    }

    public static string GetDataDirectory(string workspacePath) =>
        Path.Combine(NormalizeWorkspace(workspacePath), "System", "Data");

    public WorkspaceDataLocation? ReadLocation()
    {
        var path = Path.Combine(_profileDirectory, LocationFileName);
        if (!File.Exists(path))
        {
            return null;
        }

        if (new FileInfo(path).Length > 64 * 1024)
        {
            throw new InvalidDataException("工作目录配置文件过大，请重新选择工作目录。");
        }

        var location = JsonSerializer.Deserialize<WorkspaceDataLocation>(
            File.ReadAllText(path), JsonOptions);
        if (location is null || location.SchemaVersion != 1)
        {
            throw new InvalidDataException("工作目录配置无效，请重新选择工作目录。");
        }

        return location with { WorkspacePath = NormalizeWorkspace(location.WorkspacePath) };
    }

    public void RememberWorkspace(string workspacePath, bool initialized)
    {
        var location = new WorkspaceDataLocation(1, NormalizeWorkspace(workspacePath), initialized);
        Directory.CreateDirectory(_profileDirectory);
        WriteDocument(Path.Combine(_profileDirectory, LocationFileName), location);
    }

    public void MarkInitialized(string workspacePath)
    {
        var dataDirectory = GetDataDirectory(workspacePath);
        if (!File.Exists(Path.Combine(dataDirectory, "cdsi.db")) ||
            !File.Exists(Path.Combine(dataDirectory, "reader.db")))
        {
            throw new InvalidOperationException("两个数据库均初始化成功后才能记录工作目录状态。");
        }

        WriteDocument(Path.Combine(dataDirectory, StateMarkerFileName), new { schemaVersion = 1 });
        RememberWorkspace(workspacePath, initialized: true);
    }

    public static bool HasExistingState(string workspacePath)
    {
        var dataDirectory = GetDataDirectory(workspacePath);
        return File.Exists(Path.Combine(dataDirectory, StateMarkerFileName)) ||
            File.Exists(Path.Combine(dataDirectory, "cdsi.db")) ||
            File.Exists(Path.Combine(dataDirectory, "reader.db")) ||
            File.Exists(Path.Combine(dataDirectory, "cdsi.db-wal")) ||
            File.Exists(Path.Combine(dataDirectory, "reader.db-wal")) ||
            Directory.Exists(Path.Combine(dataDirectory, "StateProtection")) ||
            HasFiles(Path.Combine(workspacePath, "System", "DatabaseBackups"), "*.db") ||
            HasFiles(Path.Combine(workspacePath, "System", "DatabaseBackups", "Reader"), "*.db") ||
            HasFiles(Path.Combine(workspacePath, "System", "StateBackups"), "*.cdsibak");
    }

    public static string EnsureDataDirectory(string workspacePath) =>
        StateProtectionPathGuard.EnsureDirectory(
            NormalizeWorkspace(workspacePath), GetDataDirectory(workspacePath));

    public static bool RequiresExistingState(string workspacePath, WorkspaceDataLocation? remembered) =>
        HasExistingState(workspacePath) ||
        (remembered is { Initialized: true } && StateProtectionPathGuard.PathsEqual(
            workspacePath, remembered.WorkspacePath));

    public string? ReadLegacyWorkspacePath()
    {
        var databasePath = Path.Combine(_profileDirectory, "cdsi.db");
        if (!File.Exists(databasePath))
        {
            return null;
        }

        try
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false
            }.ToString());
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT w.path FROM managed_workspaces w JOIN devices d ON w.device_id = d.id " +
                "ORDER BY d.created_at LIMIT 1;";
            var path = command.ExecuteScalar() as string;
            return string.IsNullOrWhiteSpace(path) ? null : NormalizeWorkspace(path);
        }
        catch (SqliteException)
        {
            // A damaged old database must not prevent selecting a workspace for recovery.
            return null;
        }
    }

    /// <summary>Publishes a verified pair together. Sources are retained, including their WAL state.</summary>
    public async Task ImportAsync(
        string workspacePath,
        string? assetDatabasePath,
        string? readerDatabasePath,
        string applicationVersion,
        Func<string, Task>? prepareStagedData = null,
        CancellationToken cancellationToken = default)
    {
        if (assetDatabasePath is null && readerDatabasePath is null)
        {
            throw new ArgumentException("请选择至少一个可恢复的数据库。");
        }

        var workspace = NormalizeWorkspace(workspacePath);
        var target = GetDataDirectory(workspace);
        EnsureTargetIsEmpty(workspace, target);
        var systemDirectory = StateProtectionPathGuard.EnsureDirectory(
            workspace, Path.Combine(workspace, "System"));
        var staging = StateProtectionPathGuard.EnsureDirectory(
            workspace, Path.Combine(systemDirectory, $".data-import-{Guid.NewGuid():N}"));
        try
        {
            await CopyDatabaseAsync(assetDatabasePath, staging, "cdsi.db", "asset",
                applicationVersion, cancellationToken);
            await CopyDatabaseAsync(readerDatabasePath, staging, "reader.db", "reader",
                applicationVersion, cancellationToken);
            if (prepareStagedData is not null)
            {
                await prepareStagedData(staging);
                SqliteConnection.ClearAllPools();
                foreach (var (name, role) in new[] { ("cdsi.db", "asset"), ("reader.db", "reader") })
                {
                    var stagedDatabase = Path.Combine(staging, name);
                    if (File.Exists(stagedDatabase))
                    {
                        await StateBundleArchive.ValidateSqliteDatabaseAsync(
                            stagedDatabase, role, expectedSchemaVersion: null, cancellationToken);
                    }
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            EnsureTargetIsEmpty(workspace, target);
            WriteDocument(Path.Combine(staging, StateMarkerFileName), new { schemaVersion = 1 });
            if (Directory.Exists(target))
            {
                // Only an empty, plain destination directory may be replaced.
                Directory.Delete(target, recursive: false);
            }

            Directory.Move(staging, target);
        }
        finally
        {
            _ = StateProtectionPathGuard.TryDeleteDirectory(workspace, staging);
        }
    }

    public static string? FindLatestSnapshot(string workspacePath, bool reader)
    {
        var directory = Path.Combine(workspacePath, "System", "DatabaseBackups");
        if (reader)
        {
            directory = Path.Combine(directory, "Reader");
        }

        return Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "cdsi-*.db")
                .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
                .FirstOrDefault()
            : null;
    }

    public static async Task ValidateSnapshotAsync(
        string path, bool reader, CancellationToken cancellationToken = default)
    {
        var manifestPath = $"{path}.json";
        if (!File.Exists(manifestPath) || new FileInfo(manifestPath).Length > 64 * 1024)
        {
            throw new InvalidDataException("数据库快照缺少有效的校验清单。");
        }

        var manifest = JsonSerializer.Deserialize<LocalDatabaseBackupManifest>(
            await File.ReadAllTextAsync(manifestPath, cancellationToken));
        if (manifest is null || manifest.FormatVersion != 1 ||
            manifest.DatabaseFileName != Path.GetFileName(path) ||
            manifest.DatabaseSize != new FileInfo(path).Length)
        {
            throw new InvalidDataException("数据库快照的文件名或大小与清单不符。");
        }

        await using var stream = File.OpenRead(path);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
        if (!string.Equals(hash, manifest.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("数据库快照 SHA-256 校验失败。");
        }

        await StateBundleArchive.ValidateSqliteDatabaseAsync(
            path, reader ? "reader" : "asset", expectedSchemaVersion: null, cancellationToken);
    }

    private static async Task CopyDatabaseAsync(
        string? source, string staging, string name, string role,
        string version, CancellationToken cancellationToken)
    {
        if (source is null)
        {
            return;
        }

        var destination = Path.Combine(staging, name);
        await new LocalDatabaseBackupService(source, version)
            .CreateVerifiedSnapshotFileAsync(destination, cancellationToken);
        await StateBundleArchive.ValidateSqliteDatabaseAsync(
            destination, role, expectedSchemaVersion: null, cancellationToken);
    }

    private static void EnsureTargetIsEmpty(string workspace, string target)
    {
        if (File.Exists(target))
        {
            throw new IOException("数据目录被文件占用。");
        }

        if (Directory.Exists(target))
        {
            StateProtectionPathGuard.ValidateExistingDirectory(workspace, target);
            if (Directory.EnumerateFileSystemEntries(target).Any())
            {
                throw new IOException("目标工作目录已包含数据，不能覆盖或自动合并。请选择其他目录，或打开其已有数据。");
            }
        }
    }

    private static bool HasFiles(string directory, string pattern) =>
        Directory.Exists(directory) && Directory.EnumerateFiles(directory, pattern).Any();

    private static string NormalizeWorkspace(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            throw new InvalidDataException("工作目录必须使用完整的绝对路径。");
        }

        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    private static void WriteDocument<T>(string path, T document)
    {
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew,
                       FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, document, JsonOptions);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }
}

public sealed record WorkspaceDataLocation(int SchemaVersion, string WorkspacePath, bool Initialized);
