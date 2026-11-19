namespace Lumina.Services.Clock;

/// <summary>
/// 番茄钟节奏口径：休息时长由专注时长按比例派生，而不是固定 5 分钟。
/// <para>
/// 固定 5 分钟在短专注（例如 2 秒的联调/演示）下会出现“第一轮 2 秒、第二轮 5 分钟”的割裂感；
/// 这里统一按专注时长的 20% 计算（25 分钟 → 5 分钟，保持经典节奏），并限制在 1 秒–5 分钟。
/// </para>
/// </summary>
public static class PomodoroTimingPolicy
{
    public static readonly TimeSpan MinimumRest = TimeSpan.FromSeconds(1);

    public static readonly TimeSpan MaximumRest = TimeSpan.FromMinutes(5);

    /// <summary>按专注时长计算休息时长；专注时长为 0 时返回 0（调用方自行拒绝启动）。</summary>
    public static TimeSpan RestFor(TimeSpan focus)
    {
        if (focus <= TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        var rest = TimeSpan.FromTicks((long)(focus.Ticks * 0.2));

        if (rest < MinimumRest)
        {
            return MinimumRest;
        }

        return rest > MaximumRest ? MaximumRest : rest;
    }

    /// <summary>提示文案：把派生规则讲清楚，避免用户以为“第二轮被改成了 5 分钟”。</summary>
    public static string DescribeRest(TimeSpan focus)
    {
        var rest = RestFor(focus);
        return rest.TotalSeconds < 60
            ? $"{(int)rest.TotalSeconds} 秒"
            : $"{(int)rest.TotalMinutes} 分钟";
    }
}
