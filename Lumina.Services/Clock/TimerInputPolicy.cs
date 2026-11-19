using System.Globalization;

namespace Lumina.Services.Clock;

/// <summary>
/// 计时器 / 番茄钟的目标时长输入口径：小时、分钟、秒可分别输入任意非负整数，
/// 由本策略统一归一化（例如 0 小时 75 分 0 秒 → 1 小时 15 分 0 秒），避免逐项 0–59 截断。
/// </summary>
public static class TimerInputPolicy
{
    public const int DefaultCycles = 4;
    public const int MinCycles = 1;
    public const int MaxCycles = 12;

    /// <summary>读取并归一化时长；空输入按 0 处理，非法或总和为 0 返回 false。</summary>
    public static bool TryParse(
        string? hoursText,
        string? minutesText,
        string? secondsText,
        out TimeSpan target)
    {
        target = TimeSpan.Zero;

        if (!TryReadPart(hoursText, out var hours)
            || !TryReadPart(minutesText, out var minutes)
            || !TryReadPart(secondsText, out var seconds))
        {
            return false;
        }

        var totalSeconds = (long)hours * 3600 + (long)minutes * 60 + seconds;
        if (totalSeconds <= 0 || totalSeconds > TimeSpan.MaxValue.TotalSeconds)
        {
            return false;
        }

        target = TimeSpan.FromSeconds(totalSeconds);
        return true;
    }

    /// <summary>把归一化后的时长拆回三个输入框。</summary>
    public static (int Hours, int Minutes, int Seconds) Split(TimeSpan target)
    {
        var total = target < TimeSpan.Zero ? TimeSpan.Zero : target;
        var hours = (int)Math.Floor(total.TotalHours);
        return (hours, total.Minutes, total.Seconds);
    }

    /// <summary>循环次数钳制到 1–12；非法输入回落到默认 4 轮。</summary>
    public static int ParseCycles(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return DefaultCycles;
        }

        return int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var cycles)
            ? Math.Clamp(cycles, MinCycles, MaxCycles)
            : DefaultCycles;
    }

    private static bool TryReadPart(string? text, out int value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(text.Trim()))
        {
            return true;
        }

        if (!int.TryParse(
                text.Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var parsed)
            || parsed < 0)
        {
            return false;
        }

        value = parsed;
        return true;
    }
}
