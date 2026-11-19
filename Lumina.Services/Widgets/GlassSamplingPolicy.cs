namespace Lumina.Services.Widgets;

/// <summary>
/// 小组件玻璃层的采样节拍：静止时 400ms（2.5fps）省电，出现活动时 33ms（30fps），
/// 活动停止 <see cref="IdleHoldMs"/> 毫秒后自动回落。
/// 这里只做纯时间决策，便于单测；定时器与活动检测在 <c>Lumina.Widgets</c> 中。
/// </summary>
public sealed class GlassSamplingPolicy
{
    /// <summary>静止时的采样间隔：2.5fps，静态桌面无需更高刷新，降低空闲抓屏与分配。</summary>
    public const int IdleIntervalMs = 400;

    /// <summary>活动时的采样间隔：30fps，跟手拖动与视频。</summary>
    public const int ActiveIntervalMs = 33;

    /// <summary>最后一次活动后保持高频的时长。</summary>
    public const int IdleHoldMs = 1200;

    private long? _lastActivityMs;

    /// <summary>当前采样间隔（毫秒）。</summary>
    public int CurrentIntervalMs { get; private set; } = IdleIntervalMs;

    /// <summary>是否处于高频阶段。</summary>
    public bool IsActive { get; private set; }

    /// <summary>
    /// 推进到 <paramref name="nowMs"/> 并返回本帧应使用的间隔。
    /// 首次调用与检测到活动时都会刷新活动时间，因此启动后有一小段高频预热。
    /// </summary>
    public int Advance(long nowMs, bool activity)
    {
        if (activity || _lastActivityMs is null)
        {
            _lastActivityMs = nowMs;
        }

        IsActive = nowMs - _lastActivityMs.Value < IdleHoldMs;
        CurrentIntervalMs = IsActive ? ActiveIntervalMs : IdleIntervalMs;
        return CurrentIntervalMs;
    }

    /// <summary>背景帧比对发现画面在动时调用，等同于一次活动。</summary>
    public void NotifyActivity(long nowMs) => _lastActivityMs = nowMs;

    /// <summary>回到初始状态：下一次 <see cref="Advance"/> 会重新预热。</summary>
    public void Reset()
    {
        _lastActivityMs = null;
        IsActive = false;
        CurrentIntervalMs = IdleIntervalMs;
    }
}
