using CDSI.Agent.WinForms;

namespace CDSI.Agent.WinForms.Tests;

public sealed class StateDatabaseWriteGateTests
{
    [Fact]
    public async Task SuspendAsync_WaitsForActiveWriteAndRejectsNewWrites()
    {
        var gate = new StateDatabaseWriteGate();
        var writeStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseWrite = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var firstWrite = gate.TryRunAsync(async () =>
        {
            writeStarted.SetResult();
            await releaseWrite.Task;
        });
        await writeStarted.Task;

        var suspensionTask = gate.SuspendAsync();

        Assert.True(gate.IsSuspended);
        Assert.False(suspensionTask.IsCompleted);
        var rejectedWriteRan = false;
        Assert.False(await gate.TryRunAsync(() =>
        {
            rejectedWriteRan = true;
            return Task.CompletedTask;
        }));
        Assert.False(rejectedWriteRan);

        releaseWrite.SetResult();
        Assert.True(await firstWrite);
        var suspension = await suspensionTask;
        suspension.Dispose();

        Assert.False(gate.IsSuspended);
        Assert.True(await gate.TryRunAsync(() => Task.CompletedTask));
    }

    [Fact]
    public async Task FailedWrite_ReleasesTheActivityCount()
    {
        var gate = new StateDatabaseWriteGate();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            gate.TryRunAsync(() =>
                throw new InvalidOperationException("write failed")));

        var suspension = await gate.SuspendAsync();
        Assert.True(gate.IsSuspended);
        suspension.Dispose();
        Assert.False(gate.IsSuspended);
    }
}
