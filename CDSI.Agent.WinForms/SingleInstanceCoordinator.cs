using System.Security.Principal;

namespace CDSI.Agent.WinForms;

internal sealed class SingleInstanceCoordinator : IDisposable
{
    private readonly Mutex _globalMutex;
    private readonly Mutex? _legacyLocalMutex;
    private readonly EventWaitHandle _globalActivationEvent;
    private readonly EventWaitHandle _legacyLocalActivationEvent;
    private readonly bool _ownsGlobalMutex;
    private readonly bool _ownsLegacyLocalMutex;
    private readonly bool _signalLegacyLocalInstance;
    private RegisteredWaitHandle? _globalActivationRegistration;
    private RegisteredWaitHandle? _legacyLocalActivationRegistration;
    private int _listening;
    private int _disposed;

    public SingleInstanceCoordinator(string applicationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationId);
        using var currentIdentity = WindowsIdentity.GetCurrent();
        var userSid = currentIdentity.User?.Value;
        if (string.IsNullOrWhiteSpace(userSid))
        {
            throw new InvalidOperationException(
                "无法确定当前 Windows 用户，Beacon 已阻止启动以保护本地数据库。");
        }

        var globalInstanceName = $"Global\\{applicationId}.{userSid}";
        var legacyLocalInstanceName = $"Local\\{applicationId}";
        Mutex? globalMutex = null;
        Mutex? legacyLocalMutex = null;
        EventWaitHandle? globalActivationEvent = null;
        EventWaitHandle? legacyLocalActivationEvent = null;
        var ownsGlobalMutex = false;
        var ownsLegacyLocalMutex = false;
        try
        {
            globalMutex = new Mutex(
                initiallyOwned: true,
                $"{globalInstanceName}.Mutex",
                out ownsGlobalMutex);
            if (ownsGlobalMutex)
            {
                legacyLocalMutex = new Mutex(
                    initiallyOwned: true,
                    $"{legacyLocalInstanceName}.Mutex",
                    out ownsLegacyLocalMutex);
            }

            globalActivationEvent = new EventWaitHandle(
                initialState: false,
                EventResetMode.AutoReset,
                $"{globalInstanceName}.Activate");
            legacyLocalActivationEvent = new EventWaitHandle(
                initialState: false,
                EventResetMode.AutoReset,
                $"{legacyLocalInstanceName}.Activate");

            IsPrimaryInstance = ownsGlobalMutex && ownsLegacyLocalMutex;
            _signalLegacyLocalInstance =
                ownsGlobalMutex && !ownsLegacyLocalMutex;
            if (!IsPrimaryInstance)
            {
                if (ownsGlobalMutex)
                {
                    globalMutex.ReleaseMutex();
                    ownsGlobalMutex = false;
                }

                if (ownsLegacyLocalMutex)
                {
                    legacyLocalMutex?.ReleaseMutex();
                    ownsLegacyLocalMutex = false;
                }
            }

            _globalMutex = globalMutex ?? throw new InvalidOperationException(
                "未能创建 Beacon 全局单实例锁。");
            _legacyLocalMutex = legacyLocalMutex;
            _globalActivationEvent = globalActivationEvent ??
                throw new InvalidOperationException("未能创建 Beacon 全局激活事件。");
            _legacyLocalActivationEvent = legacyLocalActivationEvent ??
                throw new InvalidOperationException("未能创建 Beacon 兼容激活事件。");
            _ownsGlobalMutex = ownsGlobalMutex;
            _ownsLegacyLocalMutex = ownsLegacyLocalMutex;
        }
        catch (Exception exception)
        {
            if (ownsLegacyLocalMutex)
            {
                legacyLocalMutex?.ReleaseMutex();
            }

            if (ownsGlobalMutex)
            {
                globalMutex?.ReleaseMutex();
            }

            legacyLocalActivationEvent?.Dispose();
            globalActivationEvent?.Dispose();
            legacyLocalMutex?.Dispose();
            globalMutex?.Dispose();
            throw new InvalidOperationException(
                "无法建立 Beacon 单实例锁，已阻止启动以保护本地数据库。",
                exception);
        }
    }

    public bool IsPrimaryInstance { get; }

    public void SignalPrimaryInstance()
    {
        ThrowIfDisposed();
        if (IsPrimaryInstance)
        {
            throw new InvalidOperationException("主实例不能向自身发送激活请求。");
        }

        if (_signalLegacyLocalInstance)
        {
            _legacyLocalActivationEvent.Set();
        }
        else
        {
            _globalActivationEvent.Set();
        }
    }

    public void StartListening(Action activationHandler)
    {
        ArgumentNullException.ThrowIfNull(activationHandler);
        ThrowIfDisposed();
        if (!IsPrimaryInstance)
        {
            throw new InvalidOperationException(
                "只有主实例可以监听窗口激活请求。");
        }

        if (Interlocked.Exchange(ref _listening, 1) != 0)
        {
            throw new InvalidOperationException("激活请求监听已启动。");
        }

        try
        {
            _globalActivationRegistration = RegisterActivationHandler(
                _globalActivationEvent,
                activationHandler);
            _legacyLocalActivationRegistration = RegisterActivationHandler(
                _legacyLocalActivationEvent,
                activationHandler);
        }
        catch
        {
            Interlocked.Exchange(ref _globalActivationRegistration, null)?
                .Unregister(waitObject: null);
            Interlocked.Exchange(ref _legacyLocalActivationRegistration, null)?
                .Unregister(waitObject: null);
            Volatile.Write(ref _listening, 0);
            throw;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Interlocked.Exchange(ref _globalActivationRegistration, null)?
            .Unregister(waitObject: null);
        Interlocked.Exchange(ref _legacyLocalActivationRegistration, null)?
            .Unregister(waitObject: null);
        _globalActivationEvent.Dispose();
        _legacyLocalActivationEvent.Dispose();
        if (_ownsLegacyLocalMutex)
        {
            _legacyLocalMutex!.ReleaseMutex();
        }

        if (_ownsGlobalMutex)
        {
            _globalMutex.ReleaseMutex();
        }

        _legacyLocalMutex?.Dispose();
        _globalMutex.Dispose();
    }

    private RegisteredWaitHandle RegisterActivationHandler(
        EventWaitHandle activationEvent,
        Action activationHandler) =>
        ThreadPool.RegisterWaitForSingleObject(
            activationEvent,
            (_, timedOut) =>
            {
                if (!timedOut && Volatile.Read(ref _disposed) == 0)
                {
                    activationHandler();
                }
            },
            state: null,
            Timeout.Infinite,
            executeOnlyOnce: false);

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposed) != 0,
            this);
    }
}
