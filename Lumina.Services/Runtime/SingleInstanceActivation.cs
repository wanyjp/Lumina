using System.Threading;

namespace Lumina.Services.Runtime;

/// <summary>
/// 单实例激活信号：首个实例监听命名事件，后续实例只发送信号后退出，
/// 由首个实例把主窗口唤起，而不是弹出“已在运行”打断用户。
/// </summary>
public sealed class SingleInstanceActivation : IDisposable
{
    public const string DefaultEventName = @"Local\Lumina.Activate";

    private readonly EventWaitHandle _event;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Thread _thread;
    private readonly Action _onActivated;
    private bool _disposed;

    public SingleInstanceActivation(Action onActivated, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(onActivated);

        var resolved = ResolveEventName(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(resolved);

        _onActivated = onActivated;
        _event = new EventWaitHandle(initialState: false, EventResetMode.AutoReset, resolved);
        _thread = new Thread(Listen) { IsBackground = true, Name = "Lumina.Activation" };
        _thread.Start();
    }

    /// <summary>向已存在的首个实例发送激活信号；没有监听者时返回 false。</summary>
    public static bool TrySignal(string? name = null) => TrySignal(out _, name);

    /// <summary>
    /// 发送激活信号并带回失败原因；高完整性实例的命名事件可能拒绝中等完整性进程打开，
    /// 此时返回 false 而不是抛出异常。
    /// </summary>
    public static bool TrySignal(out Exception? error, string? name = null)
    {
        error = null;
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            using var handle = EventWaitHandle.OpenExisting(ResolveEventName(name));
            handle.Set();
            return true;
        }
        catch (Exception ex)
        {
            error = ex;
            return false;
        }
    }

    /// <summary>
    /// 事件名必须与互斥量名区分：Windows 内核对象命名空间跨类型共享，
    /// 同名互斥量会导致 EventWaitHandle 创建/打开失败（A-128）。
    /// </summary>
    private static string ResolveEventName(string? name)
        => SingleInstanceGuard.ResolveName(name) + ".Activate";

    private void Listen()
    {
        while (!_cancellation.IsCancellationRequested)
        {
            bool signaled;
            try
            {
                signaled = _event.WaitOne(200);
            }
            catch (ObjectDisposedException)
            {
                // 退出阶段句柄已释放：直接结束监听，不再访问事件对象。
                return;
            }

            if (signaled && !_cancellation.IsCancellationRequested)
            {
                try
                {
                    _onActivated();
                }
                catch (Exception)
                {
                    // 进程退出阶段回调可能失败，监听线程不得因此崩溃。
                }
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cancellation.Cancel();

        // 先唤醒等待中的监听线程，确保它退出后再释放句柄。
        try
        {
            _event.Set();
        }
        catch (ObjectDisposedException)
        {
            // 已释放则忽略。
        }

        if (!_thread.Join(TimeSpan.FromSeconds(2)))
        {
            // 线程仍存活时放弃释放事件句柄（由进程退出回收），
            // 避免后台线程 WaitOne 抛 ObjectDisposedException 终止进程。
            _cancellation.Dispose();
            return;
        }

        _event.Dispose();
        _cancellation.Dispose();
    }
}
