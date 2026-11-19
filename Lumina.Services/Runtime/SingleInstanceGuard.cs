using System.Threading;

namespace Lumina.Services.Runtime;

/// <summary>
/// 单实例守卫：以命名互斥量保证同一会话只有一个拾光进程。
/// 互斥量归当前线程所有，因此获取与释放必须在同一线程（WPF 中为 UI 线程）。
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    public const string DefaultMutexName = @"Local\Lumina.SingleInstance";

    /// <summary>性能测试/便携调试可用独立实例名，避免与受保护的历史进程互斥。</summary>
    public const string InstanceNameOverrideVariable = "LUMINA_INSTANCE_NAME";

    private readonly string _name;
    private Mutex? _mutex;
    private bool _owns;
    private bool _disposed;

    public SingleInstanceGuard(string? name = null)
    {
        _name = ResolveName(name);
    }

    /// <summary>最近一次获取失败的原因；成功或无竞争者时为 null。</summary>
    public Exception? LastError { get; private set; }

    /// <summary>上一次获取是否从已崩溃实例的废弃互斥量恢复。</summary>
    public bool RecoveredFromAbandoned { get; private set; }

    public static string ResolveName(string? name = null)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        var overridden = Environment.GetEnvironmentVariable(InstanceNameOverrideVariable);
        return string.IsNullOrWhiteSpace(overridden)
            ? DefaultMutexName
            : @"Local\" + overridden.Trim();
    }

    /// <summary>
    /// 尝试获取所有权；超时为零表示立即返回。
    /// 高完整性实例持有命名对象时，普通权限启动可能在创建/等待处收到拒绝访问；
    /// 此类异常不再向上冒泡，而是记录到 <see cref="LastError"/> 并返回 false。
    /// </summary>
    public bool TryAcquire(TimeSpan? timeout = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_owns)
        {
            return true;
        }

        try
        {
            _mutex ??= new Mutex(initiallyOwned: false, _name);
            _owns = _mutex.WaitOne(timeout ?? TimeSpan.Zero, exitContext: false);
            LastError = null;
            return _owns;
        }
        catch (AbandonedMutexException)
        {
            // 上一个实例异常退出但内核已把所有权转给当前线程，视为获取成功。
            _owns = true;
            LastError = null;
            RecoveredFromAbandoned = true;
            return true;
        }
        catch (Exception ex)
        {
            _owns = false;
            LastError = ex;
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_owns && _mutex is not null)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // 所有权可能已被内核回收（进程退出竞态），不阻断关闭。
            }

            _owns = false;
        }

        _mutex?.Dispose();
        _disposed = true;
    }
}
