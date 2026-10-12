using System.IO.Pipes;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace CDSI.Agent.WinForms;

internal sealed class ShellFileImportBroker : IDisposable
{
    private readonly object _sync = new();
    private readonly Queue<string[]> _pending = new();
    private readonly NamedPipeServerStream _server;
    private readonly CancellationTokenSource _stopping = new();
    private readonly Task _listener;
    private bool _accepting = true;
    private int _pendingFileCount;

    public ShellFileImportBroker(string applicationId)
    {
        _server = new NamedPipeServerStream(GetPipeName(applicationId), PipeDirection.InOut,
            1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly | PipeOptions.FirstPipeInstance);
        _listener = ListenAsync();
    }

    public int PendingFileCount { get { lock (_sync) { return _pendingFileCount; } } }

    public bool TryEnqueue(IReadOnlyCollection<string> paths)
    {
        var normalized = ShellFileImportRequest.NormalizePaths(paths);
        lock (_sync)
        {
            if (!_accepting || _pendingFileCount + normalized.Length > 1_000)
            {
                return false;
            }
            _pending.Enqueue(normalized);
            _pendingFileCount += normalized.Length;
            return true;
        }
    }

    public string[] Drain()
    {
        lock (_sync)
        {
            var result = _pending.SelectMany(paths => paths)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            _pending.Clear();
            _pendingFileCount = 0;
            return result;
        }
    }

    public void StopAccepting()
    {
        lock (_sync) { _accepting = false; }
    }

    public static async Task SendAsync(string applicationId, IReadOnlyCollection<string> paths)
    {
        var normalized = ShellFileImportRequest.NormalizePaths(paths);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var client = new NamedPipeClientStream(".", GetPipeName(applicationId),
            PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            await client.ConnectAsync(timeout.Token).ConfigureAwait(false);
            var payload = JsonSerializer.SerializeToUtf8Bytes(normalized);
            await client.WriteAsync(BitConverter.GetBytes(payload.Length), timeout.Token).ConfigureAwait(false);
            await client.WriteAsync(payload, timeout.Token).ConfigureAwait(false);
            await client.FlushAsync(timeout.Token).ConfigureAwait(false);
            var response = new byte[1];
            await client.ReadExactlyAsync(response, timeout.Token).ConfigureAwait(false);
            if (response[0] != 1)
            {
                throw new IOException("Beacon 正在退出或登记队列已满，请稍后重试。");
            }
        }
        catch (OperationCanceledException exception)
        {
            throw new IOException("运行中的 Beacon 暂时无法接收文件；旧版本请先退出后重试。", exception);
        }
    }

    private async Task ListenAsync()
    {
        try
        {
            while (!_stopping.IsCancellationRequested)
            {
                await _server.WaitForConnectionAsync(_stopping.Token).ConfigureAwait(false);
                try
                {
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_stopping.Token);
                    deadline.CancelAfter(TimeSpan.FromSeconds(5));
                    var header = new byte[4];
                    await _server.ReadExactlyAsync(header, deadline.Token).ConfigureAwait(false);
                    var length = BitConverter.ToInt32(header);
                    if (length is < 1 or > ShellFileImportRequest.MaximumPayloadBytes)
                    {
                        throw new InvalidDataException("登记请求长度无效。");
                    }
                    var payload = new byte[length];
                    await _server.ReadExactlyAsync(payload, deadline.Token).ConfigureAwait(false);
                    var paths = JsonSerializer.Deserialize<string[]>(payload)
                        ?? throw new InvalidDataException("登记请求为空。");
                    var accepted = TryEnqueue(paths);
                    await _server.WriteAsync(new byte[] { accepted ? (byte)1 : (byte)0 },
                        deadline.Token).ConfigureAwait(false);
                    await _server.FlushAsync(deadline.Token).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is IOException or InvalidDataException or JsonException or
                    ArgumentException or OperationCanceledException)
                {
                    // Reject a broken client without stopping the next file's request.
                }
                finally
                {
                    if (_server.IsConnected) { _server.Disconnect(); }
                }
            }
        }
        catch (Exception exception) when (_stopping.IsCancellationRequested &&
            exception is OperationCanceledException or ObjectDisposedException or IOException)
        {
            // Normal shutdown interrupts the outstanding accept/read.
        }
    }

    internal static string GetPipeName(string applicationId)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var sid = identity.User?.Value ?? throw new InvalidOperationException("无法确定 Windows 用户。");
        return "CDSI.Beacon.FileImport." + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(applicationId + "|" + sid)));
    }

    public void Dispose()
    {
        StopAccepting();
        _stopping.Cancel();
        _server.Dispose();
        // The listener never invokes UI code, so it is safe to drain it during disposal.
        try { _listener.GetAwaiter().GetResult(); }
        finally { _stopping.Dispose(); }
    }
}
