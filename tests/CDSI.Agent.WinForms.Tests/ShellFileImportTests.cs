using System.IO.Pipes;
using Microsoft.Win32;
using CDSI.Agent.WinForms;

namespace CDSI.Agent.WinForms.Tests;

public sealed class ShellFileImportTests
{
    [Fact]
    public async Task Broker_ForwardsConcurrentSelectionsAndDeduplicatesWithoutAnotherAppInstance()
    {
        var id = "CDSI.Beacon.Tests." + Guid.NewGuid().ToString("N");
        using var broker = new ShellFileImportBroker(id);
        var file = Path.Combine(Path.GetTempPath(), "中文 素材 & one.txt");
        var other = Path.Combine(Path.GetTempPath(), "two.txt");

        await Task.WhenAll(ShellFileImportBroker.SendAsync(id, [file]),
            ShellFileImportBroker.SendAsync(id, [other]),
            ShellFileImportBroker.SendAsync(id, [file]));

        Assert.Equal(3, broker.PendingFileCount);
        Assert.Equal(new[] { file, other }.Order(), broker.Drain().Order());
        Assert.Equal(0, broker.PendingFileCount);
    }

    [Fact]
    public async Task Broker_ClosingRejectsNewRequestsRatherThanLosingThemSilently()
    {
        var id = "CDSI.Beacon.Tests." + Guid.NewGuid().ToString("N");
        using var broker = new ShellFileImportBroker(id);
        broker.StopAccepting();

        await Assert.ThrowsAsync<IOException>(() => ShellFileImportBroker.SendAsync(id,
            [Path.Combine(Path.GetTempPath(), "item.txt")]));

        Assert.Empty(broker.Drain());
    }

    [Fact]
    public void Broker_QueueLimitRejectsARequestWithoutDiscardingEarlierRequests()
    {
        using var broker = new ShellFileImportBroker("CDSI.Beacon.Tests." + Guid.NewGuid().ToString("N"));
        for (var index = 0; index < 1_000; index++)
        {
            Assert.True(broker.TryEnqueue([Path.Combine(Path.GetTempPath(), $"{index}.txt")]));
        }
        Assert.False(broker.TryEnqueue([Path.Combine(Path.GetTempPath(), "overflow.txt")]));
        Assert.Equal(1_000, broker.Drain().Length);
    }

    [Fact]
    public async Task Broker_RejectsOversizedPayloadAndStillAcceptsTheNextFile()
    {
        var id = "CDSI.Beacon.Tests." + Guid.NewGuid().ToString("N");
        using var broker = new ShellFileImportBroker(id);
        await using (var client = new NamedPipeClientStream(".", ShellFileImportBroker.GetPipeName(id),
            PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly))
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await client.ConnectAsync(timeout.Token);
            await client.WriteAsync(BitConverter.GetBytes(ShellFileImportRequest.MaximumPayloadBytes + 1),
                timeout.Token);
            await client.FlushAsync(timeout.Token);
            var response = new byte[1];
            Assert.Equal(0, await client.ReadAsync(response, timeout.Token));
        }
        var file = Path.Combine(Path.GetTempPath(), "next.txt");
        await ShellFileImportBroker.SendAsync(id, [file]);
        Assert.Equal([file], broker.Drain());
    }

    [Fact]
    public void Arguments_PreserveUnicodeAndSpecialCharactersAndRejectRelativeOrEmptyInput()
    {
        var file = Path.Combine(Path.GetTempPath(), "中文 空格 & # draft.md");
        Assert.Equal([file], ShellFileImportRequest.ParseArguments(["--import-file", file, file]));
        Assert.Throws<ArgumentException>(() => ShellFileImportRequest.ParseArguments(["--import-file"]));
        Assert.Throws<ArgumentException>(() => ShellFileImportRequest.ParseArguments(["--import-file", "relative.md"]));
        Assert.Empty(ShellFileImportRequest.ParseArguments([]));
    }

    [Fact]
    public void Registry_EnableUpdateAndDisableTouchOnlyTheDedicatedUserRegistration()
    {
        var testKey = @"Software\CDSI\Beacon\Tests\" + Guid.NewGuid().ToString("N");
        var tempPath = Path.Combine(Path.GetTempPath(), "cdsi-agent-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempPath);
        var executable = Path.Combine(tempPath, "中文 Beacon.exe");
        File.WriteAllText(executable, "fixture - never executed");
        var service = new ExplorerContextMenuService(testKey + @"\verb", notifyExplorer: false);
        try
        {
            Assert.Null(service.GetRegisteredExecutable());
            using (var sibling = Registry.CurrentUser.CreateSubKey(testKey + @"\unrelated"))
            {
                sibling.SetValue("keep", "untouched");
            }
            service.Apply(true, executable);
            Assert.Equal(executable, service.GetRegisteredExecutable());
            using (var key = Registry.CurrentUser.OpenSubKey(testKey + @"\verb\command"))
            {
                Assert.Equal($"\"{executable}\" --import-file \"%1\"", key?.GetValue(""));
            }
            service.Apply(false, executable);
            Assert.Null(service.GetRegisteredExecutable());
            using var unrelated = Registry.CurrentUser.OpenSubKey(testKey + @"\unrelated");
            Assert.Equal("untouched", unrelated?.GetValue("keep"));
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(testKey, throwOnMissingSubKey: false);
            Directory.Delete(tempPath, recursive: true);
        }
    }

    [Fact]
    public void Registry_DoesNotRemoveAForeignEntryWithTheSameName()
    {
        var testKey = @"Software\CDSI\Beacon\Tests\" + Guid.NewGuid().ToString("N");
        try
        {
            using (var key = Registry.CurrentUser.CreateSubKey(testKey)) { key.SetValue("", "foreign"); }
            var service = new ExplorerContextMenuService(testKey, notifyExplorer: false);
            Assert.Throws<InvalidOperationException>(() => service.Apply(false, "unused.exe"));
            using var retained = Registry.CurrentUser.OpenSubKey(testKey);
            Assert.Equal("foreign", retained?.GetValue(""));
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(testKey, throwOnMissingSubKey: false); }
    }
}
