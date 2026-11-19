namespace Lumina.Services.Contracts;

/// <summary>时钟与世界时钟。实现于 P1-01 / P1-02。</summary>
public interface IClockService
{
    ClockSnapshot Now(bool use12Hour, bool shichenMode);

    IReadOnlyList<WorldClockItem> WorldClocks();

    event Action? TickSecond;
}

/// <summary>计时器。实现于 P1-03；暂停/恢复必须基于结束时刻或剩余时间。</summary>
public interface ITimerEngine
{
    TimerState State { get; }

    /// <summary>已记录的标记时间点（相对开始的累计时长）；主界面与小组件共用同一份。</summary>
    IReadOnlyList<TimeSpan> Marks { get; }

    void Start(TimeSpan target);

    void Pause();

    void Resume();

    void Reset();

    /// <summary>记录当前已过时间点；空闲状态下不产生标记。重置时一并清空。</summary>
    void Mark();

    event Action<TimerState>? Changed;
}

/// <summary>秒表。实现于 P1-03；厘秒精度、计圈与累计时间。</summary>
public interface IStopwatchEngine
{
    StopwatchState State { get; }

    IReadOnlyList<TimeSpan> Laps { get; }

    void Start();

    void Pause();

    void Reset();

    void MarkLap();

    event Action<StopwatchState>? Changed;
}

/// <summary>
/// 番茄钟。实现于 P1-03；会话结束只发布一次事件并带幂等 ID。
/// P1-03 新增暂停/恢复，用于与计时器共享同一组控制按钮。
/// </summary>
public interface IPomodoroService
{
    PomodoroState Current { get; }

    void Start(TimeSpan focus, TimeSpan rest, int cycles);

    void Pause();

    void Resume();

    void ForceStop();

    event Action<PomodoroState>? Updated;
}
