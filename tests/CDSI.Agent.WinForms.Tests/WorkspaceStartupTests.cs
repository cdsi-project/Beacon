namespace CDSI.Agent.WinForms.Tests;

public sealed class WorkspaceStartupTests
{
    [Fact]
    public void ProfileOverride_RequiresAbsolutePathAndKeepsTheDefaultPerUser()
    {
        Assert.Equal(Path.Combine(Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData), "CDSI"),
            Program.GetProfileDirectory(null));
        Assert.Throws<ArgumentException>(() => Program.GetProfileDirectory("relative-profile"));
        var isolated = Path.Combine(Path.GetTempPath(), "cdsi-agent-tests", Guid.NewGuid().ToString("N"));
        Assert.Equal(isolated, Program.GetProfileDirectory(isolated));
    }

    [Fact]
    public void ReconnectDialog_PrefillsTheOriginalWorkspaceInsteadOfTheDefault()
    {
        var originalWorkspace = Path.Combine(Path.GetTempPath(), "existing-workspace");
        using var form = new FirstRunSetupForm(originalWorkspace);
        Assert.Equal(originalWorkspace, form.SelectedPath);
    }

    [Fact]
    public void SystemDriveSelection_RequiresTheSystemVolumeWarning()
    {
        var systemRoot = Path.GetPathRoot(Environment.GetFolderPath(
            Environment.SpecialFolder.Windows))!;
        Assert.True(WorkspaceStartup.IsOnSystemDrive(Path.Combine(systemRoot, "workspace")));
        var otherRoot = string.Equals(systemRoot, "D:\\", StringComparison.OrdinalIgnoreCase)
            ? "E:\\" : "D:\\";
        Assert.False(WorkspaceStartup.IsOnSystemDrive(Path.Combine(otherRoot, "workspace")));
    }
}
