using Lumina.Domain.Enums;

namespace Lumina.Services.Widgets;

/// <summary>
/// 逐帧捕获的运行状态（进程内共享）：组件窗口在捕获启动成功、失败回退时上报，
/// 总设置页据此在开关下方用红字说明“为什么退回了 0.2 秒采样”。
///
/// 所有成员线程安全；<see cref="Changed"/> 在调用方线程触发，界面需自行切回 UI 线程。
/// </summary>
public static class WidgetGlassCaptureStatus
{
    private static readonly object Sync = new();
    private static readonly Dictionary<WidgetKind, string> Fallbacks = [];

    /// <summary>回退状态发生变化（新增、恢复或清除）。</summary>
    public static event EventHandler? Changed;

    /// <summary>
    /// 当前的回退原因；多个组件同时回退时去重后用「；」连接。全部正常时为 null。
    /// </summary>
    public static string? FallbackReason
    {
        get
        {
            lock (Sync)
            {
                if (Fallbacks.Count == 0)
                {
                    return null;
                }

                var reasons = Fallbacks
                    .OrderBy(pair => pair.Key)
                    .Select(pair => pair.Value)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                return string.Join("；", reasons);
            }
        }
    }

    /// <summary>某个组件的捕获已正常运行（会清掉它之前的回退原因）。</summary>
    public static void ReportActive(WidgetKind kind) => Remove(kind);

    /// <summary>某个组件回退到采样模式，记下原因供设置页显示。</summary>
    public static void ReportFallback(WidgetKind kind, string reason)
    {
        ArgumentNullException.ThrowIfNull(reason);

        var changed = false;
        lock (Sync)
        {
            if (!Fallbacks.TryGetValue(kind, out var current) || current != reason)
            {
                Fallbacks[kind] = reason;
                changed = true;
            }
        }

        Raise(changed);
    }

    /// <summary>某个组件不再参与逐帧捕获（换皮肤、关闭组件、关掉开关）。</summary>
    public static void ReportInactive(WidgetKind kind) => Remove(kind);

    /// <summary>开关关闭或会话整体重建时清空。</summary>
    public static void Reset()
    {
        var changed = false;
        lock (Sync)
        {
            if (Fallbacks.Count > 0)
            {
                Fallbacks.Clear();
                changed = true;
            }
        }

        Raise(changed);
    }

    private static void Remove(WidgetKind kind)
    {
        var changed = false;
        lock (Sync)
        {
            changed = Fallbacks.Remove(kind);
        }

        Raise(changed);
    }

    private static void Raise(bool changed)
    {
        if (changed)
        {
            Changed?.Invoke(null, EventArgs.Empty);
        }
    }
}
