namespace CDSI.Agent.WinForms;

internal sealed class StateDatabaseWriteGate
{
    private readonly object _sync = new();
    private int _activeOperations;
    private bool _suspended;
    private TaskCompletionSource? _drained;

    public bool IsSuspended
    {
        get
        {
            lock (_sync)
            {
                return _suspended;
            }
        }
    }

    public async Task<bool> TryRunAsync(Func<Task> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        lock (_sync)
        {
            if (_suspended)
            {
                return false;
            }

            _activeOperations++;
        }

        try
        {
            await operation();
            return true;
        }
        finally
        {
            TaskCompletionSource? drained = null;
            lock (_sync)
            {
                _activeOperations--;
                if (_activeOperations == 0)
                {
                    drained = _drained;
                    _drained = null;
                }
            }

            drained?.TrySetResult();
        }
    }

    public async Task<IDisposable> SuspendAsync()
    {
        Task waitForDrain;
        lock (_sync)
        {
            if (_suspended)
            {
                throw new InvalidOperationException(
                    "状态数据库写入已处于暂停状态。");
            }

            _suspended = true;
            if (_activeOperations == 0)
            {
                waitForDrain = Task.CompletedTask;
            }
            else
            {
                _drained = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                waitForDrain = _drained.Task;
            }
        }

        await waitForDrain;
        return new Suspension(this);
    }

    private void Resume()
    {
        lock (_sync)
        {
            if (!_suspended)
            {
                return;
            }

            _suspended = false;
        }
    }

    private sealed class Suspension(StateDatabaseWriteGate owner) : IDisposable
    {
        private StateDatabaseWriteGate? _owner = owner;

        public void Dispose()
        {
            Interlocked.Exchange(ref _owner, null)?.Resume();
        }
    }
}
