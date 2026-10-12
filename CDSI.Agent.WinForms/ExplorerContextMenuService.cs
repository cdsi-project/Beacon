using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace CDSI.Agent.WinForms;

internal sealed class ExplorerContextMenuService
{
    internal const string VerbPath = @"Software\Classes\*\shell\CDSI.Beacon.AddAsset";
    private const string Owner = "CDSI.Beacon.FileImport.v1";
    private readonly string _verbPath;
    private readonly bool _notifyExplorer;

    public ExplorerContextMenuService(string? verbPath = null, bool notifyExplorer = true)
    {
        _verbPath = verbPath ?? VerbPath;
        _notifyExplorer = notifyExplorer;
    }

    public string? GetRegisteredExecutable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(_verbPath);
        return key?.GetValue("BeaconOwner") as string == Owner
            ? key.GetValue("BeaconExecutable") as string : null;
    }

    public void Apply(bool enabled, string executablePath)
    {
        using (var existing = Registry.CurrentUser.OpenSubKey(_verbPath))
        {
            if (existing is not null && existing.GetValue("BeaconOwner") as string != Owner)
            {
                throw new InvalidOperationException("同名右键菜单项不属于 Beacon，已保留现有配置。");
            }
        }

        if (enabled)
        {
            var executable = ValidateExecutable(executablePath);
            using var key = Registry.CurrentUser.CreateSubKey(_verbPath);
            key.SetValue("BeaconOwner", Owner);
            key.SetValue("BeaconExecutable", executable);
            key.SetValue("", "加入 Beacon 资源管理");
            key.SetValue("Icon", $"\"{executable}\",0");
            // Explorer dispatches one command per selected file; the primary instance queues them.
            key.SetValue("MultiSelectModel", "Player");
            using var command = key.CreateSubKey("command");
            command.SetValue("", CreateCommand(executable));
        }
        else
        {
            // This dedicated per-user verb is the only registration Beacon removes.
            Registry.CurrentUser.DeleteSubKeyTree(_verbPath, throwOnMissingSubKey: false);
        }
        if (_notifyExplorer)
        {
            SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
        }
    }

    internal static string CreateCommand(string executablePath) =>
        $"\"{ValidateExecutable(executablePath)}\" --import-file \"%1\"";

    private static string ValidateExecutable(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) ||
            path.Contains('"') || path.Contains('\0') ||
            !string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Path.GetFileName(path), "dotnet.exe", StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(path))
        {
            throw new ArgumentException("请从可用的 Beacon EXE 启用右键菜单，并保持程序路径稳定。");
        }
        return Path.GetFullPath(path);
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);
}
