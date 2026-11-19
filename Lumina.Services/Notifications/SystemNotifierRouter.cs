using Lumina.Services.Contracts;

namespace Lumina.Services.Notifications;

/// <summary>
/// 系统通知路由器：服务层只依赖抽象，托盘创建后由 App 注入真实通道；
/// 未注入或已分离时返回 false，调度器据此降级为应用内提醒。
/// </summary>
public sealed class SystemNotifierRouter : INotifier
{
    private volatile INotifier? _inner;

    public void Attach(INotifier notifier)
    {
        ArgumentNullException.ThrowIfNull(notifier);
        _inner = notifier;
    }

    public void Detach() => _inner = null;

    public Task<bool> ShowToastAsync(
        string title,
        string body,
        string? route = null,
        CancellationToken cancellationToken = default)
    {
        var inner = _inner;

        return inner is null
            ? Task.FromResult(false)
            : inner.ShowToastAsync(title, body, route, cancellationToken);
    }
}
